namespace CameraMaintainer.Core.Models;

/// <summary>
/// Tracks firmware version changes over time for audit and downgrade detection.
/// A new record is created each time the firmware version differs from the last known value.
/// </summary>
public class FirmwareRecord
{
    public int Id { get; set; }
    public int CameraId { get; set; }
    public string FirmwareVersion { get; set; } = string.Empty;
    public string HardwareId { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether this version is considered current/up-to-date.</summary>
    public bool IsCurrent { get; set; }

    /// <summary>Navigation property.</summary>
    public Camera? Camera { get; set; }
}
