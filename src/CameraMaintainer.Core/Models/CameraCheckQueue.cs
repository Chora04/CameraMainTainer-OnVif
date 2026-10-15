using CameraMaintainer.Core.Enums;

namespace CameraMaintainer.Core.Models;

public sealed record CameraCheckQueueItem(
    int CameraId,
    string Name,
    string? IpAddress,
    CameraStatus Status,
    int Position,
    bool AutomaticChecksEnabled,
    int CheckIntervalSeconds,
    DateTime? LastCheckAt,
    bool HasPhysicalFault,
    int ConsecutiveHealthyChecks = 0,
    bool IsStable = false,
    int? EffectiveCheckIntervalSeconds = null,
    bool StableChecksPaused = false,
    long NextCycleNumber = 1,
    int StableCheckEveryCycles = 2,
    int? CyclesUntilCheck = 1);

public sealed record CameraCheckCycle(long Number, IReadOnlyList<int> CameraIds, bool HasEnabledCameras);

public sealed record CameraCheckQueueUpdate(
    int CameraId,
    int Position,
    bool AutomaticChecksEnabled,
    int CheckIntervalSeconds);
