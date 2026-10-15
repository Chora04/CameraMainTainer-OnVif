namespace CameraMaintainer.Infrastructure.Data;

public static class CameraAlertLocks
{
    // Bounded locks coordinate monitoring, firmware events and manual closure.
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    public static SemaphoreSlim ForCamera(int? cameraId) => Gates[(int)((uint)(cameraId ?? 0) % (uint)Gates.Length)];
}
