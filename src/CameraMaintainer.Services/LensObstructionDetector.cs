using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Services;

/// <summary>
/// Downloads and analyzes camera snapshots to detect lens obstruction
/// and determine camera view direction.
///
/// Detection pipeline (v2 — SkiaSharp based):
/// 1. Download JPEG snapshot via IFrameProcessor
/// 2. Decode to grayscale pixels via TurboJPEG (SkiaSharp)
/// 3. Analyze scene via CameraDirectionAnalyzer (Sobel + LBP + histogram)
/// 4. Classify into obstruction type
///
/// This replaces the previous byte-sampling heuristic with proper pixel-level analysis.
/// </summary>
public class LensObstructionDetector
{
    private const double MinimumAlertConfidence = 0.80;

    private readonly IFrameProcessor _frameProcessor;
    private readonly ICameraDirectionAnalyzer _directionAnalyzer;
    private readonly ILogger<LensObstructionDetector>? _logger;

    public LensObstructionDetector(
        IFrameProcessor frameProcessor,
        ICameraDirectionAnalyzer directionAnalyzer,
        ILogger<LensObstructionDetector>? logger = null)
    {
        _frameProcessor = frameProcessor;
        _directionAnalyzer = directionAnalyzer;
        _logger = logger;
    }

    public record ObstructionResult(
        bool IsObstructed,
        string ObstructionType,       // "none", "dark", "bright", "blurry", "low_contrast", "wall", "narrow_slit"
        DirectionStatus DirectionStatus,
        double MeanBrightness,
        double Variance,
        double Entropy,
        double EdgeDensity,
        double TextureUniformityScore,
        double DirectionConfidence,
        string? DirectionDescription,
        string? ErrorMessage,
        long DownloadTimeMs,
        long DecodeTimeMs,
        long SnapshotSizeBytes
    )
    {
        public byte[] EncodedImage { get; init; } = [];
        public string ImageContentType { get; init; } = "image/jpeg";

        public bool ConfirmsRecovery => !IsObstructed &&
            DirectionStatus == DirectionStatus.Normal && string.IsNullOrEmpty(ErrorMessage);
    }

    /// <summary>
    /// Download snapshot and analyze for lens obstruction and direction.
    /// </summary>
    public async Task<ObstructionResult> AnalyzeAsync(
        string snapshotUri,
        int? cameraId = null,
        string? username = null,
        string? password = null,
        CancellationToken ct = default)
    {
        try
        {
            // Step 1: Capture and decode via FrameProcessor (TurboJPEG)
            var frame = await _frameProcessor.CaptureAndDecodeAsync(
                snapshotUri, username, password, maxWidth: 640, ct: ct);

            if (!frame.IsValid)
            {
                return new ObstructionResult(
                    false, "none", DirectionStatus.Unknown,
                    0, 0, 0, 0, 0, 0, null,
                    frame.ErrorMessage ?? "Frame capture failed",
                    frame.DownloadTimeMs, frame.DecodeTimeMs, frame.OriginalSizeBytes);
            }

            // Step 2: Compute statistics
            var stats = _frameProcessor.ComputeStatistics(frame.Pixels, frame.Width, frame.Height);

            // Step 3: Direction analysis (Sobel + LBP + histogram)
            var direction = _directionAnalyzer.Analyze(frame, cameraId, stats);

            // Step 4: Map DirectionStatus to obstruction type
            var (isObstructed, obstructionType) = MapToObstruction(direction);

            _logger?.LogDebug(
                "Snapshot analysis: dir={Dir}, obstructed={Obs}, type={Type}, " +
                "edges={Edge:F1}%, texture={Tex:F1}%, mean={Mean:F1}, entropy={Entropy:F2}, conf={Conf:F2}",
                direction.Status, isObstructed, obstructionType,
                direction.EdgeDensity, direction.TextureUniformityScore,
                direction.MeanBrightness, direction.Entropy, direction.Confidence);

            return new ObstructionResult(
                IsObstructed: isObstructed,
                ObstructionType: obstructionType,
                DirectionStatus: direction.Status,
                MeanBrightness: direction.MeanBrightness,
                Variance: stats.Variance,
                Entropy: direction.Entropy,
                EdgeDensity: direction.EdgeDensity,
                TextureUniformityScore: direction.TextureUniformityScore,
                DirectionConfidence: direction.Confidence,
                DirectionDescription: direction.Description,
                ErrorMessage: null,
                DownloadTimeMs: frame.DownloadTimeMs,
                DecodeTimeMs: frame.DecodeTimeMs,
                SnapshotSizeBytes: frame.OriginalSizeBytes
            )
            {
                EncodedImage = frame.EncodedImage,
                ImageContentType = frame.ContentType
            };
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Snapshot analysis failed for {Uri}", snapshotUri);
            return new ObstructionResult(
                false, "none", DirectionStatus.Unknown,
                0, 0, 0, 0, 0, 0, null,
                ex.Message, 0, 0, 0);
        }
    }

    /// <summary>
    /// Map DirectionStatus to the legacy obstruction type string.
    /// </summary>
    private static (bool isObstructed, string type) MapToObstruction(DirectionResult direction)
    {
        if (direction.Confidence < MinimumAlertConfidence)
            return (false, "none");

        return direction.Status switch
        {
            DirectionStatus.Normal => (false, "none"),
            DirectionStatus.WallObstructed => (true, "wall"),
            DirectionStatus.PartiallyObstructed => (true, "narrow_slit"),
            DirectionStatus.DarkScene => (true, "dark"),
            DirectionStatus.Overexposed => (true, "bright"),
            DirectionStatus.Unknown => (false, "none"),
            _ => (false, "none")
        };
    }
}
