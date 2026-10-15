namespace CameraMaintainer.Core.Models;

public sealed record CameraMonitoringPolicy
{
    public int HealthyChecksRequired { get; init; } = 5;
    public bool CheckStableCameras { get; init; } = true;
    public int StableCheckEveryCycles { get; init; } = 2;
    public bool StoreFrameHistory { get; init; } = true;
    public bool StoreAnomalyImages { get; init; } = true;
    public int FrameHistoryLimitPerCamera { get; init; } = 15;
    public double NormalBrightnessMinimum { get; init; } = 35;
    public double NormalBrightnessMaximum { get; init; } = 220;
    public double NormalContrastMinimum { get; init; } = 24;
    public double NormalEntropyMinimum { get; init; } = 5;

    public bool IsValid =>
        HealthyChecksRequired is >= 1 and <= 1000 &&
        StableCheckEveryCycles is >= 1 and <= 24 &&
        FrameHistoryLimitPerCamera is >= 1 and <= 100 &&
        NormalBrightnessMinimum is >= 0 and < 255 &&
        NormalBrightnessMaximum is > 0 and <= 255 &&
        NormalBrightnessMinimum < NormalBrightnessMaximum &&
        NormalContrastMinimum is >= 0 and <= 128 &&
        NormalEntropyMinimum is >= 0 and <= 8;
}
