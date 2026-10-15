using CameraMaintainer.Core.Models;

namespace CameraMaintainer.Core.Interfaces;

public interface IDiscoveryService
{
    Task<ScanRecord> ScanAsync(
        string scanType = "ws_discovery",
        string? ipRange = null,
        CancellationToken cancellationToken = default);
    Task<ScanRecord?> GetScanStatusAsync(int scanId);
}

public interface IHealthService
{
    Task<HealthRecord> CheckCameraAsync(int cameraId, CancellationToken cancellationToken = default);
    Task CheckAllDueAsync(CancellationToken cancellationToken = default);
    Task<HealthRecord?> GetCurrentHealthAsync(int cameraId);
    Task<HealthSummary> GetHealthSummaryAsync();
}

public record HealthSummary
{
    public int Total { get; set; }
    public int Online { get; set; }
    public int Suspect { get; set; }
    public int Offline { get; set; }
    public int Unknown { get; set; }
    public double? AverageTemperature { get; set; }
}

public interface IPtzService
{
    Task<List<PtzPreset>> GetPresetsAsync(int cameraId);
    Task<PtzPreset> SetPresetAsync(int cameraId, string profileToken, string presetName);
    Task GotoPresetAsync(int cameraId, string profileToken, string presetToken);
    Task MoveAbsoluteAsync(int cameraId, string profileToken, double pan, double tilt, double zoom);
    Task MoveRelativeAsync(int cameraId, string profileToken, double pan, double tilt, double zoom);
    Task StopAsync(int cameraId, string profileToken);
}

public interface IConfigService
{
    Task<NetworkConfig> GetNetworkConfigAsync(int cameraId);
    Task<List<MediaProfile>> GetMediaProfilesAsync(int cameraId);
    Task<List<UserInfo>> GetUsersAsync(int cameraId);
    Task CreateUserAsync(int cameraId, string newUsername, string newPassword, string userLevel);
    Task DeleteUserAsync(int cameraId, string targetUsername);
    Task SyncDateTimeAsync(int cameraId);
}

public interface IMaintenanceService
{
    Task<ConfigBackup> BackupConfigAsync(
        int cameraId,
        string backupType = "full",
        string createdBy = "manual",
        CancellationToken cancellationToken = default);
    Task<List<ConfigBackup>> GetBackupsAsync(int cameraId);
    Task<MaintenanceJob> RebootCameraAsync(int cameraId);
    Task<MaintenanceJob> FactoryResetCameraAsync(int cameraId, string mode = "hard");
    Task<MaintenanceJob?> GetJobStatusAsync(int jobId);
}

public interface IAlertService
{
    Task<Alert> CreateAlertAsync(
        int? cameraId,
        Enums.AlertType alertType,
        Enums.AlertSeverity severity,
        string title,
        string? description = null,
        AlertDiagnostic? diagnostic = null);
    Task<List<Alert>> GetCameraAlertHistoryAsync(int cameraId, int actorUserId, int limit = 30);
    Task<List<AlertAuditEvent>> GetAlertAuditAsync(int alertId, int actorUserId);
    Task RecordAlertViewedAsync(int alertId, int actorUserId);
    Task<Alert> AssignAlertAsync(int alertId, int assigneeUserId, int actorUserId);
    Task<Alert> MoveAlertAsync(int alertId, string stage, int actorUserId);
    Task<Alert> UpdateAlertWorkAsync(
        int alertId,
        Enums.ErrorCategory category,
        string? workReport,
        bool serviceNotified,
        int actorUserId);
    Task<Alert> ResolveAlertAsync(int alertId, int actorUserId);
    Task MarkConnectivityRecoveredAsync(int cameraId);
    Task MarkRecoveredAsync(int cameraId, Enums.AlertType alertType);
}

public interface IAlertHandoffService
{
    Task<List<AlertHandoff>> GetForAlertAsync(int alertId, int actorUserId);
    Task<List<AlertHandoffReason>> GetReasonsAsync(bool includeInactive = false);
    Task<AlertHandoff> CreateAsync(
        int alertId,
        int toUserId,
        int reasonId,
        string comment,
        int dueHours,
        int actorUserId);
    Task<AlertHandoff> ViewAsync(long handoffId, int actorUserId);
    Task<AlertHandoff> AcceptAsync(long handoffId, string? comment, int actorUserId);
    Task<AlertHandoff> ForwardAsync(
        long handoffId,
        int toUserId,
        int reasonId,
        string comment,
        int dueHours,
        int actorUserId);
    Task<AlertHandoff> CompleteAsync(long handoffId, string comment, int actorUserId);
    Task<AlertHandoff> RejectAsync(long handoffId, string comment, int actorUserId);
    Task<AlertHandoffReason> SaveReasonAsync(int? reasonId, string name, bool isActive, int actorUserId);
}

public interface IAutoRecoveryService
{
    Task<bool> TryAutoRecoverAsync(
        int cameraId,
        string statusCode,
        CancellationToken cancellationToken = default);
}
