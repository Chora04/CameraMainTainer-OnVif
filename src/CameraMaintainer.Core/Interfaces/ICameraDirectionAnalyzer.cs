using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Models;

namespace CameraMaintainer.Core.Interfaces;

/// <summary>
/// Analyzes camera frame content to determine if the camera is pointed correctly
/// or obstructed (wall, narrow slit, overexposed, etc).
/// Uses Sobel edge detection, LBP texture analysis, and histogram statistics.
/// </summary>
public interface ICameraDirectionAnalyzer
{
    /// <summary>
    /// Analyze a decoded grayscale frame and classify the camera's view direction.
    /// </summary>
    /// <param name="frame">Decoded frame with grayscale pixel data.</param>
    /// <param name="cameraId">Optional camera ID for baseline histogram tracking.</param>
    /// <returns>Direction analysis result with classification and confidence metrics.</returns>
    DirectionResult Analyze(
        FrameData frame,
        int? cameraId = null,
        ImageStatistics? statistics = null);

    /// <summary>
    /// Update the per-camera baseline histogram used for scene comparison.
    /// Called after a confirmed-normal classification to establish reference.
    /// </summary>
    void UpdateBaseline(int cameraId, int[] histogram);

    /// <summary>
    /// Get the current baseline histogram for a camera, if one exists.
    /// </summary>
    int[]? GetBaseline(int cameraId);

    /// <summary>
    /// Reset the baseline for a camera (e.g., after physical repositioning).
    /// </summary>
    void ResetBaseline(int cameraId);
}

/// <summary>
/// Result of camera direction analysis.
/// </summary>
public class DirectionResult
{
    public DirectionStatus Status { get; init; } = DirectionStatus.Unknown;
    public double Confidence { get; init; }              // 0.0 – 1.0

    /// <summary>Percentage of pixels with significant edge gradient (Sobel magnitude > threshold).</summary>
    public double EdgeDensity { get; init; }

    /// <summary>Percentage of 16×16 grid cells with uniform LBP (≤ 2 transitions).</summary>
    public double TextureUniformityScore { get; init; }

    /// <summary>Mean pixel brightness (0–255).</summary>
    public double MeanBrightness { get; init; }

    /// <summary>Standard deviation of pixel brightness.</summary>
    public double BrightnessStdDev { get; init; }

    /// <summary>Shannon entropy of luminance histogram (bits).</summary>
    public double Entropy { get; init; }

    /// <summary>Human-readable description of the analysis verdict.</summary>
    public string Description { get; init; } = string.Empty;
}
