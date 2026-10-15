using CameraMaintainer.Core.Models;

namespace CameraMaintainer.Core.Interfaces;

public interface ICameraFrameArchiveService
{
    Task<CameraFrameRecord?> RecordAsync(
        CameraFrameCapture capture,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CameraFrameRecord>> GetRecentAsync(
        int cameraId,
        CancellationToken cancellationToken = default);

    Task<CameraFrameContent?> OpenContentAsync(
        int cameraId,
        long recordId,
        CancellationToken cancellationToken = default);

    Task DeleteForCameraAsync(
        int cameraId,
        CancellationToken cancellationToken = default);
}
