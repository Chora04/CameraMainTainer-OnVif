using CameraMaintainer.Core.Models;

namespace CameraMaintainer.Core.Interfaces;

/// <summary>
/// Firmware version tracking and baseline comparison service.
/// </summary>
public interface IFirmwareService
{
    /// <summary>
    /// Record the current firmware version for a camera.
    /// Creates a FirmwareRecord if the version differs from the last known value.
    /// </summary>
    Task<FirmwareRecord?> RecordVersionAsync(int cameraId, string firmwareVersion,
        string hardwareId, string manufacturer, string model);

    /// <summary>
    /// Check if the given firmware version is considered up-to-date
    /// based on configured baseline versions per manufacturer/model.
    /// </summary>
    bool IsUpToDate(string manufacturer, string model, string firmwareVersion);

    /// <summary>
    /// Get firmware version history for a camera.
    /// </summary>
    Task<List<FirmwareRecord>> GetHistoryAsync(int cameraId, int limit = 20);

}
