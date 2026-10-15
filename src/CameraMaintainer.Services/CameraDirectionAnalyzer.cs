using System.Collections.Concurrent;
using System.Buffers;
using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Services;

/// <summary>
/// Analyzes decoded grayscale frames to determine camera view direction.
///
/// Algorithm pipeline:
/// 1. Sobel edge detection → edge density (walls have few edges)
/// 2. LBP texture analysis → uniformity score (walls are flat, scenes are varied)
/// 3. Histogram statistics → brightness, contrast, entropy
/// 4. Narrow slit detection → horizontal/vertical strip contrast analysis
/// 5. Classification fusion → weighted vote with confidence score
///
/// Per-camera baseline histograms are maintained for comparison over time.
/// </summary>
public class CameraDirectionAnalyzer : ICameraDirectionAnalyzer
{
    private readonly ILogger<CameraDirectionAnalyzer>? _logger;

    // Thresholds — calibrated for typical surveillance camera scenes
    public double EdgeDensityWallThreshold { get; set; } = 2.0;       // % — strong wall evidence
    public double EdgeDensityNormalMin { get; set; } = 4.5;           // % — typical detailed scene
    public double TextureUniformityWallThreshold { get; set; } = 90.0; // % — supporting wall evidence
    public double TextureUniformityNormalMax { get; set; } = 80.0;    // % — typical detailed scene
    public double DarkMeanThreshold { get; set; } = 20.0;              // Mean brightness below = dark
    public double BrightMeanThreshold { get; set; } = 240.0;           // Mean brightness above = overexposed
    public double LowContrastStdDevThreshold { get; set; } = 12.0;     // StdDev below = low contrast
    public double SlitContrastRatio { get; set; } = 3.0;              // Bright/dark ratio for slit detection

    // Sobel convolution kernels (3×3)
    private static readonly int[] SobelX = { -1, 0, 1, -2, 0, 2, -1, 0, 1 };
    private static readonly int[] SobelY = { -1, -2, -1, 0, 0, 0, 1, 2, 1 };

    // Per-camera baseline storage (cameraId → 256-bin histogram)
    private readonly ConcurrentDictionary<int, int[]> _baselines = new();

    public CameraDirectionAnalyzer(ILogger<CameraDirectionAnalyzer>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public DirectionResult Analyze(
        FrameData frame,
        int? cameraId = null,
        ImageStatistics? statistics = null)
    {
        if (!frame.IsValid || frame.Pixels.Length == 0)
        {
            return new DirectionResult
            {
                Status = DirectionStatus.Unknown,
                Confidence = 0,
                Description = "Invalid or empty frame"
            };
        }

        try
        {
            var width = frame.Width;
            var height = frame.Height;
            var pixels = frame.Pixels;

            // ── Step 1: Compute Sobel edge density ──
            double edgeDensity = ComputeEdgeDensity(pixels, width, height);

            // ── Step 2: LBP texture analysis on 16×16 grid ──
            double textureUniformity = ComputeLbpUniformity(pixels, width, height);

            // ── Step 3: Histogram statistics ──
            var stats = statistics ?? ComputeHistogramStats(pixels);

            // ── Step 4: Narrow slit detection ──
            double slitScore = ComputeSlitScore(pixels, width, height);

            // ── Step 5: Classification fusion ──
            var (status, confidence, description) = ClassifyFusion(
                edgeDensity, textureUniformity, stats, slitScore, cameraId);

            return new DirectionResult
            {
                Status = status,
                Confidence = Math.Round(confidence, 3),
                EdgeDensity = Math.Round(edgeDensity, 2),
                TextureUniformityScore = Math.Round(textureUniformity, 2),
                MeanBrightness = stats.Mean,
                BrightnessStdDev = stats.StdDev,
                Entropy = stats.Entropy,
                Description = description
            };
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Direction analysis failed");
            return new DirectionResult
            {
                Status = DirectionStatus.Unknown,
                Confidence = 0,
                Description = $"Analysis error: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public void UpdateBaseline(int cameraId, int[] histogram)
    {
        _baselines[cameraId] = histogram;
        _logger?.LogDebug("Baseline updated for camera {CameraId}", cameraId);
    }

    /// <inheritdoc />
    public int[]? GetBaseline(int cameraId)
    {
        return _baselines.TryGetValue(cameraId, out var baseline) ? baseline : null;
    }

    /// <inheritdoc />
    public void ResetBaseline(int cameraId)
    {
        _baselines.TryRemove(cameraId, out _);
        _logger?.LogInformation("Baseline reset for camera {CameraId}", cameraId);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Private analysis methods
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Compute Sobel edge density — percentage of pixels with gradient magnitude
    /// exceeding a dynamic threshold (mean + 2 × stddev of gradient magnitudes).
    /// Walls/uniform surfaces have very low edge density (&lt; 3%).
    /// Natural scenes have higher edge density (&gt; 8%).
    /// </summary>
    private static double ComputeEdgeDensity(byte[] pixels, int width, int height)
    {
        if (width < 3 || height < 3) return 0;

        // Down-sample: process every 2nd pixel for speed
        const int step = 2;
        int totalPixels = 0;

        var estimatedSamples = Math.Max(16, (width / step) * (height / step));
        var magnitudes = ArrayPool<double>.Shared.Rent(estimatedSamples);
        double magnitudeSum = 0;
        double magnitudeSumSquares = 0;

        try
        {
            for (int y = 1; y < height - 1; y += step)
            {
                for (int x = 1; x < width - 1; x += step)
                {
                    double gx = 0, gy = 0;
                    int ki = 0;

                    for (int ky = -1; ky <= 1; ky++)
                    {
                        for (int kx = -1; kx <= 1; kx++)
                        {
                            int idx = (y + ky) * width + (x + kx);
                            double p = pixels[idx];

                            gx += p * SobelX[ki];
                            gy += p * SobelY[ki];
                            ki++;
                        }
                    }

                    double magnitude = Math.Sqrt(gx * gx + gy * gy);
                    magnitudes[totalPixels++] = magnitude;
                    magnitudeSum += magnitude;
                    magnitudeSumSquares += magnitude * magnitude;
                }
            }

            if (totalPixels < 10) return 0;

            double meanMagnitude = magnitudeSum / totalPixels;
            double variance = magnitudeSumSquares / totalPixels - meanMagnitude * meanMagnitude;
            double threshold = meanMagnitude + 2.0 * Math.Sqrt(Math.Max(0, variance));
            int edgePixels = 0;
            for (int i = 0; i < totalPixels; i++)
            {
                if (magnitudes[i] > threshold)
                    edgePixels++;
            }

            return (double)edgePixels / totalPixels * 100.0;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(magnitudes);
        }
    }

    /// <summary>
    /// Local Binary Pattern (LBP) uniformity score on a 16×16 grid.
    ///
    /// For each grid cell, compute 8-neighbor LBP and check uniformity
    /// (number of 0→1 and 1→0 transitions ≤ 2 = uniform).
    ///
    /// Returns the percentage of grid cells with uniform LBP.
    /// Walls: high uniformity (&gt; 70%) — flat, monotonous surface.
    /// Scenes: low uniformity (&lt; 40%) — diverse textures.
    /// </summary>
    private static double ComputeLbpUniformity(byte[] pixels, int width, int height)
    {
        const int cellSize = 16;
        int gridCols = Math.Max(1, width / cellSize);
        int gridRows = Math.Max(1, height / cellSize);

        int uniformCells = 0;
        int totalCells = 0;

        for (int gy = 0; gy < gridRows; gy++)
        {
            for (int gx = 0; gx < gridCols; gx++)
            {
                int cx = gx * cellSize + cellSize / 2;
                int cy = gy * cellSize + cellSize / 2;

                if (cx < 1 || cx >= width - 1 || cy < 1 || cy >= height - 1)
                    continue;

                byte center = pixels[cy * width + cx];

                int pattern = 0;
                if (pixels[(cy - 1) * width + (cx - 1)] >= center) pattern |= 1 << 0;
                if (pixels[(cy - 1) * width + cx] >= center) pattern |= 1 << 1;
                if (pixels[(cy - 1) * width + (cx + 1)] >= center) pattern |= 1 << 2;
                if (pixels[cy * width + (cx + 1)] >= center) pattern |= 1 << 3;
                if (pixels[(cy + 1) * width + (cx + 1)] >= center) pattern |= 1 << 4;
                if (pixels[(cy + 1) * width + cx] >= center) pattern |= 1 << 5;
                if (pixels[(cy + 1) * width + (cx - 1)] >= center) pattern |= 1 << 6;
                if (pixels[cy * width + (cx - 1)] >= center) pattern |= 1 << 7;

                // Count 0↔1 transitions in the circular pattern
                int transitions = 0;
                for (int i = 0; i < 8; i++)
                {
                    int bit1 = (pattern >> i) & 1;
                    int bit2 = (pattern >> ((i + 1) % 8)) & 1;
                    if (bit1 != bit2) transitions++;
                }

                if (transitions <= 2) uniformCells++;
                totalCells++;
            }
        }

        return totalCells > 0 ? (double)uniformCells / totalCells * 100.0 : 0;
    }

    /// <summary>
    /// Detect narrow slits by analyzing horizontal and vertical strip contrast.
    ///
    /// Divides the frame into 3 horizontal and 3 vertical strips.
    /// If outer strips are dark and center strip is bright (or vice versa)
    /// with sharp contrast ratio, the camera is likely looking through a narrow gap.
    /// </summary>
    private static double ComputeSlitScore(byte[] pixels, int width, int height)
    {
        // Horizontal analysis: top, middle, bottom strips
        double hScore = AnalyzeStripContrast(pixels, width, height, horizontal: true);
        // Vertical analysis: left, center, right strips
        double vScore = AnalyzeStripContrast(pixels, width, height, horizontal: false);

        return Math.Max(hScore, vScore);
    }

    private static double AnalyzeStripContrast(byte[] pixels, int width, int height, bool horizontal)
    {
        const int numStrips = 3;
        Span<double> stripMeans = stackalloc double[numStrips];

        if (horizontal)
        {
            int stripHeight = height / numStrips;
            for (int s = 0; s < numStrips; s++)
            {
                int startY = s * stripHeight;
                int endY = (s == numStrips - 1) ? height : (s + 1) * stripHeight;
                long sum = 0;
                int count = 0;

                for (int y = startY; y < endY; y++)
                {
                    int rowStart = y * width;
                    for (int x = 0; x < width; x += 4) // Sample every 4th pixel
                    {
                        sum += pixels[rowStart + x];
                        count++;
                    }
                }

                stripMeans[s] = count > 0 ? (double)sum / count : 0;
            }
        }
        else
        {
            int stripWidth = width / numStrips;
            for (int s = 0; s < numStrips; s++)
            {
                int startX = s * stripWidth;
                int endX = (s == numStrips - 1) ? width : (s + 1) * stripWidth;
                long sum = 0;
                int count = 0;

                for (int y = 0; y < height; y++)
                {
                    int rowStart = y * width;
                    for (int x = startX; x < endX; x += 4)
                    {
                        sum += pixels[rowStart + x];
                        count++;
                    }
                }

                stripMeans[s] = count > 0 ? (double)sum / count : 0;
            }
        }

        // Check if middle strip differs significantly from outer strips
        double middleMean = stripMeans[1];
        double outerMean = (stripMeans[0] + stripMeans[2]) / 2.0;
        if (outerMean < 1) return 0;

        double contrastRatio = Math.Max(middleMean / outerMean, outerMean / middleMean);
        return Math.Min(contrastRatio / 5.0, 1.0); // Normalized 0–1 score
    }

    /// <summary>
    /// Compute basic histogram statistics from pixel data.
    /// </summary>
    private static ImageStatistics ComputeHistogramStats(byte[] pixels)
    {
        var histogram = new int[256];
        long sum = 0;
        long sumSq = 0;
        byte min = 255;
        byte max = 0;
        int count = pixels.Length;

        for (int i = 0; i < count; i++)
        {
            byte b = pixels[i];
            histogram[b]++;
            sum += b;
            sumSq += b * b;
            if (b < min) min = b;
            if (b > max) max = b;
        }

        double mean = (double)sum / count;
        double variance = ((double)sumSq / count) - (mean * mean);
        if (variance < 0) variance = 0;
        double stdDev = Math.Sqrt(variance);

        // Entropy
        double entropy = 0;
        for (int i = 0; i < 256; i++)
        {
            if (histogram[i] > 0)
            {
                double p = (double)histogram[i] / count;
                entropy -= p * Math.Log2(p);
            }
        }

        // Median
        byte median = 0;
        int cumulative = 0;
        int half = count / 2;
        for (int i = 0; i < 256; i++)
        {
            cumulative += histogram[i];
            if (cumulative >= half) { median = (byte)i; break; }
        }

        return new ImageStatistics
        {
            Histogram = histogram,
            Mean = mean,
            Variance = variance,
            StdDev = stdDev,
            Entropy = entropy,
            Min = min,
            Max = max,
            Median = median,
            SampleCount = count
        };
    }

    /// <summary>
    /// Fusion classifier — combines edge density, texture uniformity,
    /// histogram stats, and slit score into a final DirectionStatus.
    /// </summary>
    private (DirectionStatus status, double confidence, string description) ClassifyFusion(
        double edgeDensity,
        double textureUniformity,
        ImageStatistics stats,
        double slitScore,
        int? cameraId)
    {
        // ── Check for dark scene ──
        if (stats.Mean < DarkMeanThreshold)
        {
            return (DirectionStatus.DarkScene, 0.95,
                $"Too dark: mean brightness={stats.Mean:F1} < {DarkMeanThreshold}");
        }

        // ── Check for overexposed ──
        if (stats.Mean > BrightMeanThreshold && stats.StdDev < LowContrastStdDevThreshold * 2)
        {
            return (DirectionStatus.Overexposed, 0.90,
                $"Overexposed: mean={stats.Mean:F1} > {BrightMeanThreshold}, stddev={stats.StdDev:F1}");
        }

        // ── Check for narrow slit ──
        if (slitScore > 0.7)
        {
            double conf = 0.60 + (slitScore - 0.7) * 0.9; // 0.60–0.87
            return (DirectionStatus.PartiallyObstructed, Math.Min(conf, 0.95),
                $"Narrow slit detected: contrast score={slitScore:F2} — camera may be looking through a tight gap");
        }

        // ── Wall obstruction: low edges + high uniformity ──
        int wallScore = 0;
        if (edgeDensity < EdgeDensityWallThreshold) wallScore += 3;
        else if (edgeDensity < EdgeDensityNormalMin) wallScore += 1;

        if (textureUniformity > TextureUniformityWallThreshold) wallScore += 3;
        else if (textureUniformity > TextureUniformityNormalMax) wallScore += 1;

        if (stats.StdDev < LowContrastStdDevThreshold) wallScore += 2;
        if (stats.Entropy < 4.0) wallScore += 1;

        bool hasHealthySceneDetail = stats.StdDev >= 24.0 && stats.Entropy >= 5.5;

        if (wallScore >= 6)
        {
            double conf = 0.70 + (wallScore - 6) * 0.07; // 0.70–0.98
            return (DirectionStatus.WallObstructed, Math.Min(conf, 0.98),
                $"Wall obstructed: edges={edgeDensity:F1}% (threshold={EdgeDensityWallThreshold}%), " +
                $"texture uniformity={textureUniformity:F1}% (threshold={TextureUniformityWallThreshold}%), " +
                $"contrast stddev={stats.StdDev:F1}");
        }
        else if (wallScore >= 3 && !hasHealthySceneDetail)
        {
            return (DirectionStatus.Unknown, 0.40,
                $"Ambiguous scene: edges={edgeDensity:F1}%, texture uniformity={textureUniformity:F1}%, " +
                $"stddev={stats.StdDev:F1}, entropy={stats.Entropy:F2}");
        }

        // ── Normal scene ──
        double normalConf = 0.70;
        if (edgeDensity > EdgeDensityNormalMin) normalConf += 0.08;
        if (textureUniformity < TextureUniformityNormalMax) normalConf += 0.07;
        if (stats.StdDev >= 24.0) normalConf += 0.05;
        if (stats.Entropy > 5.5) normalConf += 0.05;

        // Update baseline if this is a strong normal classification
        if (cameraId.HasValue && normalConf > 0.85 && edgeDensity > EdgeDensityNormalMin)
        {
            UpdateBaseline(cameraId.Value, stats.Histogram);
        }

        return (DirectionStatus.Normal, Math.Min(normalConf, 0.95),
            $"Normal scene: edges={edgeDensity:F1}%, texture uniformity={textureUniformity:F1}%, " +
            $"brightness mean={stats.Mean:F1}, entropy={stats.Entropy:F2}");
    }
}
