namespace CameraMaintainer.Core.Models;

/// <summary>
/// Decoded grayscale frame data from a camera snapshot.
/// Contains raw luminance values and metadata for analysis.
/// </summary>
public record FrameData
{
    /// <summary>Original encoded image bytes. Kept only for the current check.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[] EncodedImage { get; init; } = Array.Empty<byte>();

    /// <summary>Grayscale pixel values (0–255), row-major order.</summary>
    public byte[] Pixels { get; init; } = Array.Empty<byte>();

    /// <summary>Frame width in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Frame height in pixels.</summary>
    public int Height { get; init; }

    /// <summary>Original JPEG byte size before decode.</summary>
    public long OriginalSizeBytes { get; init; }

    /// <summary>Time taken to download the JPEG (milliseconds).</summary>
    public long DownloadTimeMs { get; init; }

    /// <summary>Time taken to decode the JPEG (milliseconds).</summary>
    public long DecodeTimeMs { get; init; }

    /// <summary>MIME type of the original image.</summary>
    public string ContentType { get; init; } = "image/jpeg";

    /// <summary>Whether the frame was successfully decoded.</summary>
    public bool IsValid { get; init; }

    /// <summary>Error message if decode failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Snapshot URI used to fetch this frame.</summary>
    public string? SourceUri { get; init; }

    /// <summary>Total pixel count (Width × Height).</summary>
    public int PixelCount => Width * Height;
}

/// <summary>
/// Basic image statistics computed from grayscale pixel data.
/// </summary>
public class ImageStatistics
{
    /// <summary>256-bin luminance histogram.</summary>
    public int[] Histogram { get; init; } = new int[256];

    /// <summary>Mean pixel value (0–255).</summary>
    public double Mean { get; init; }

    /// <summary>Variance of pixel values.</summary>
    public double Variance { get; init; }

    /// <summary>Standard deviation of pixel values.</summary>
    public double StdDev { get; init; }

    /// <summary>Shannon entropy of the luminance histogram (bits).</summary>
    public double Entropy { get; init; }

    /// <summary>Minimum pixel value.</summary>
    public byte Min { get; init; }

    /// <summary>Maximum pixel value.</summary>
    public byte Max { get; init; }

    /// <summary>Median pixel value.</summary>
    public byte Median { get; init; }

    /// <summary>Number of pixels sampled for statistics.</summary>
    public int SampleCount { get; init; }
}
