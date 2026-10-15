using System.Text.Json.Serialization;

namespace CameraMaintainer.Core.Models;

public sealed class CameraFrameRecord
{
    public long Id { get; set; }
    public int CameraId { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public bool IsAnomalous { get; set; }
    public string TriggerCodes { get; set; } = string.Empty;
    public string DirectionStatus { get; set; } = "Unknown";
    public string ObstructionType { get; set; } = "none";
    public double MeanBrightness { get; set; }
    public double BrightnessStdDev { get; set; }
    public double Entropy { get; set; }
    public double EdgeDensity { get; set; }
    public double TextureUniformityScore { get; set; }
    public double DirectionConfidence { get; set; }
    public string? ImageRelativePath { get; set; }
    public string? ImageContentType { get; set; }
    public long? ImageSizeBytes { get; set; }

    [JsonIgnore]
    public Camera? Camera { get; set; }
}

public sealed record CameraFrameCapture
{
    public int CameraId { get; init; }
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;
    public bool IsObstructed { get; init; }
    public string ObstructionType { get; init; } = "none";
    public string DirectionStatus { get; init; } = "Unknown";
    public double MeanBrightness { get; init; }
    public double BrightnessStdDev { get; init; }
    public double Entropy { get; init; }
    public double EdgeDensity { get; init; }
    public double TextureUniformityScore { get; init; }
    public double DirectionConfidence { get; init; }
    public byte[] EncodedImage { get; init; } = [];
    public string ImageContentType { get; init; } = "image/jpeg";
}

public sealed record CameraFrameContent(Stream Stream, string ContentType, string FileName);
