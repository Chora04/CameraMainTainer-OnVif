namespace CameraMaintainer.Core.Interfaces;

public interface IMonitoringEvents
{
    Task HealthUpdatedAsync(int cameraId);
    Task AlertsChangedAsync();
    Task CameraRegistryChangedAsync();
    Task MaintenanceChangedAsync();
}
