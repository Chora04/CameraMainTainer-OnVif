using CameraMaintainer.Core.Models;

namespace CameraMaintainer.Core.Interfaces;

/// <summary>
/// JPEG frame processing pipeline using TurboJPEG (via SkiaSharp).
/// Handles capture, decode, analysis, and re-compression with minimal RAM usage.
/// </summary>
public interface IFrameProcessor
{
    /// <summary>
    /// Download a JPEG snapshot from the camera and decode to raw grayscale pixels.
    /// Uses SkiaSharp (libjpeg-turbo) for fast decode — under 2ms for 1080p.
    /// </summary>
    Task<FrameData> CaptureAndDecodeAsync(
        string snapshotUri,
        string? username = null,
        string? password = null,
        int maxWidth = 640,
        CancellationToken ct = default);

    /// <summary>
    /// Decode JPEG bytes to grayscale pixel buffer.
    /// Returns frame metadata and raw luminance values (0–255 per pixel).
    /// </summary>
    FrameData DecodeJpegBytes(byte[] jpegBytes, int maxWidth = 640);

    /// <summary>
    /// Re-compress raw pixel data to JPEG with configurable quality.
    /// Useful for bandwidth-constrained transmission.
    /// </summary>
    byte[] EncodeToJpeg(byte[] grayPixels, int width, int height, int quality = 75);

    /// <summary>
    /// Fast luminance histogram computation on grayscale pixels.
    /// Returns 256-bin histogram and basic statistics.
    /// </summary>
    ImageStatistics ComputeStatistics(byte[] grayPixels, int width, int height, int sampleStep = 1);
}
