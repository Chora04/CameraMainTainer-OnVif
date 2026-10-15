using CameraMaintainer.Core.Enums;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CameraMaintainer.Core.Models;

public class Camera
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? FirmwareVersion { get; set; }

    public string ServiceUrl { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public int Port { get; set; } = 80;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? StreetAddress { get; set; }

    public CameraStatus Status { get; set; } = CameraStatus.Unknown;
    public DateTime? LastSeenAt { get; set; }
    public DateTime? LastCheckAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveHealthyChecks { get; set; }

    public double? TemperatureC { get; set; }
    public int? UptimeSeconds { get; set; }

    public bool PtzSupported { get; set; }
    public bool EventsSupported { get; set; }
    public string? CapabilitiesRaw { get; set; }

    public string Tags { get; set; } = "[]";
    public string Notes { get; set; } = string.Empty;

    public bool AutoBackup { get; set; } = true;
    public bool AutoRecovery { get; set; } = true;
    // One complete monitoring round is planned for the one-hour window.
    public int CheckIntervalSeconds { get; set; } = 3600;
    public bool AutomaticChecksEnabled { get; set; } = true;
    public int? CheckQueuePosition { get; set; }
    public int NextAlertSequence { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Direction / obstruction analysis
    public string DirectionStatus { get; set; } = "Unknown";
    public DateTime? DirectionLastCheckedAt { get; set; }
    public double? LastEdgeDensity { get; set; }
    public double? LastTextureScore { get; set; }
    public string BaselineHistogramJson { get; set; } = "{}";

    // Navigation properties
    [JsonIgnore]
    public Credential? Credential { get; set; }
    [JsonIgnore]
    public HealthRecord? CurrentHealth { get; set; }
    [JsonIgnore]
    public List<PtzPreset> PtzPresets { get; set; } = new();
    [JsonIgnore]
    public List<ConfigBackup> ConfigBackups { get; set; } = new();
    [JsonIgnore]
    public List<Alert> Alerts { get; set; } = new();
    [JsonIgnore]
    public List<CameraFrameRecord> FrameRecords { get; set; } = new();
}

public class Credential
{
    public int Id { get; set; }
    public int CameraId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordEnc { get; set; } = string.Empty;
    public string AuthType { get; set; } = "wsse";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public Camera? Camera { get; set; }
}

public class HealthRecord
{
    public int Id { get; set; }
    public int CameraId { get; set; }
    public bool Reachable { get; set; }
    public string? StatusCode { get; set; }
    public int ResponseTimeMs { get; set; }
    public double? TemperatureC { get; set; }
    public int? UptimeSeconds { get; set; }
    public string? ErrorMessage { get; set; }
    public string DetailJson { get; set; } = "{}";
    public double? JitterMs { get; set; }
    public double? PacketLossPercent { get; set; }
    public long? SnapshotSizeBytes { get; set; }
    public string DirectionStatus { get; set; } = "Unknown";
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public Camera? Camera { get; set; }
}

public class PtzPreset
{
    public int Id { get; set; }
    public int CameraId { get; set; }
    public string ProfileToken { get; set; } = string.Empty;
    public string PresetToken { get; set; } = string.Empty;
    public string PresetName { get; set; } = string.Empty;

    [JsonIgnore]
    public Camera? Camera { get; set; }
}

public class ConfigBackup
{
    public int Id { get; set; }
    public int CameraId { get; set; }
    public string BackupType { get; set; } = "full";
    [JsonIgnore]
    public string ConfigXml { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = "auto";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public Camera? Camera { get; set; }
}

public class MaintenanceJob
{
    public int Id { get; set; }
    public string JobType { get; set; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public string CameraIds { get; set; } = "[]";
    public string ParamsJson { get; set; } = "{}";
    public int TotalCount { get; set; }
    public int CompletedCount { get; set; }
    public int FailedCount { get; set; }
    public string ErrorsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class Alert
{
    public int Id { get; set; }
    public int? CameraId { get; set; }
    public int? CameraSequenceNumber { get; set; }
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
    public AlertType AlertType { get; set; }
    public ErrorCategory Category { get; set; } = ErrorCategory.Technical;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool AutoResolved { get; set; }
    public int? AssignedToUserId { get; set; }
    public string? AssignedToUsername { get; set; }
    public string? AssignedToDisplayName { get; set; }
    public string? AssignedByUsername { get; set; }
    public DateTime? AssignedAt { get; set; }
    public string? WorkReport { get; set; }
    public string? WorkflowStage { get; set; } = "new";
    public bool ServiceNotified { get; set; }
    public string? ServiceNotifiedByUsername { get; set; }
    public DateTime? ServiceNotifiedAt { get; set; }
    public string? ResolvedByUsername { get; set; }
    public string? ResolvedByDisplayName { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? RecoveryDetectedAt { get; set; }
    public int? MergedIntoAlertId { get; set; }
    public ICollection<AlertIssue> Issues { get; set; } = new List<AlertIssue>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public Camera? Camera { get; set; }

    [JsonIgnore]
    public ICollection<AlertAuditEvent> AuditEvents { get; set; } = new List<AlertAuditEvent>();

    [JsonIgnore]
    public ICollection<AlertHandoff> Handoffs { get; set; } = new List<AlertHandoff>();

    [JsonIgnore]
    public ICollection<UserNotification> Notifications { get; set; } = new List<UserNotification>();
}

public sealed class AlertAuditEvent
{
    private static readonly JsonSerializerOptions AuditJsonOptions = CreateAuditJsonOptions();

    public long Id { get; set; }
    public int AlertId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int? ActorUserId { get; set; }
    public string ActorUsername { get; set; } = "system";
    public string ActorDisplayName { get; set; } = "Система";
    public string Summary { get; set; } = string.Empty;
    public string? FromStage { get; set; }
    public string? ToStage { get; set; }
    public string? PreviousValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public Alert? Alert { get; set; }

    public static string CaptureState(Alert alert)
    {
        return JsonSerializer.Serialize(new
        {
            alert.AlertType,
            alert.Severity,
            alert.Category,
            alert.Title,
            alert.Description,
            alert.AutoResolved,
            alert.AssignedToUserId,
            alert.AssignedToUsername,
            alert.AssignedToDisplayName,
            alert.AssignedByUsername,
            alert.AssignedAt,
            alert.WorkReport,
            alert.WorkflowStage,
            alert.ServiceNotified,
            alert.ServiceNotifiedByUsername,
            alert.ServiceNotifiedAt,
            alert.ResolvedByUsername,
            alert.ResolvedByDisplayName,
            alert.ResolvedAt,
            alert.RecoveryDetectedAt,
            issues = alert.Issues.Select(issue => new
            {
                issue.AlertType,
                issue.Category,
                issue.Severity,
                issue.Title,
                issue.Description,
                issue.FailureCode,
                issue.FailureStage,
                issue.FailureDurationMs,
                issue.FailureTimeoutMs,
                issue.StartedAt,
                issue.RecoveredAt,
                issue.ResolvedByUserId,
                issue.ResolvedByUsername,
                issue.ResolvedByDisplayName
            })
        }, AuditJsonOptions);
    }

    private static JsonSerializerOptions CreateAuditJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public sealed class AlertIssue
{
    public int Id { get; set; }
    public int AlertId { get; set; }
    public AlertType AlertType { get; set; }
    public ErrorCategory Category { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureStage { get; set; }
    public int? FailureDurationMs { get; set; }
    public int? FailureTimeoutMs { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RecoveredAt { get; set; }
    public int? ResolvedByUserId { get; set; }
    public string? ResolvedByUsername { get; set; }
    public string? ResolvedByDisplayName { get; set; }
    public ICollection<AlertIssueMessage> Messages { get; set; } = new List<AlertIssueMessage>();
    [JsonIgnore]
    public Alert? Alert { get; set; }

    public static AlertIssue FromAlert(Alert alert) => new()
    {
        AlertType = alert.AlertType,
        Category = alert.Category,
        Severity = alert.Severity,
        Title = alert.Title,
        Description = alert.Description,
        StartedAt = alert.CreatedAt,
        RecoveredAt = alert.RecoveryDetectedAt ?? (alert.AutoResolved ? alert.ResolvedAt : null),
        ResolvedByUsername = alert.AutoResolved ? alert.ResolvedByUsername : null,
        ResolvedByDisplayName = alert.AutoResolved ? alert.ResolvedByDisplayName : null
    };
}

public sealed class AlertIssueMessage
{
    public long Id { get; set; }
    public int AlertIssueId { get; set; }
    public int AuthorUserId { get; set; }
    public string AuthorUsername { get; set; } = string.Empty;
    public string AuthorDisplayName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public AlertIssue? AlertIssue { get; set; }
}

public sealed record AlertDiagnostic
{
    public string? FailureCode { get; init; }
    public string? FailureStage { get; init; }
    public int? DurationMs { get; init; }
    public int? TimeoutMs { get; init; }
}

public class ScanRecord
{
    public int Id { get; set; }
    public string ScanType { get; set; } = "ws_discovery";
    public int CamerasFound { get; set; }
    public int CamerasAdded { get; set; }
    public string Status { get; set; } = "completed";
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Setting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AlertBoardColumn
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Color { get; set; } = "#f0a43b";
    public List<string> VisibleToRoles { get; set; } = AlertBoardRoleVisibility.All();
    public int SortOrder { get; set; }
}

public static class AlertBoardRoleVisibility
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string Technician = "Technician";
    public const string Viewer = "Viewer";

    public static IReadOnlyList<string> AllowedRoles { get; } =
        [Admin, Operator, Technician, Viewer];

    public static List<string> All() => [.. AllowedRoles];

    public static List<string> For(params string[] roles) => Normalize(roles);

    public static List<string> Normalize(IEnumerable<string>? roles)
    {
        if (roles == null)
            return All();

        var selected = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        selected.Add(Admin);
        return AllowedRoles.Where(selected.Contains).ToList();
    }

    public static bool IsVisibleTo(
        AlertBoardColumn column,
        string? role,
        bool isAdministrator,
        string? accountColumnKeys = null)
    {
        if (isAdministrator)
            return true;
        if (string.IsNullOrWhiteSpace(role))
            return false;

        var roleAllowsColumn = column.VisibleToRoles?.Any(item =>
            item.Equals(role, StringComparison.OrdinalIgnoreCase)) == true;
        if (!roleAllowsColumn || accountColumnKeys == null)
            return roleAllowsColumn;

        return ParseAccountColumnKeys(accountColumnKeys).Contains(column.Key);
    }

    public static HashSet<string> ParseAccountColumnKeys(string? value) =>
        (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(key => key.Length <= 64)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static string? SerializeAccountColumnKeys(IEnumerable<string>? keys)
    {
        if (keys == null)
            return null;

        return string.Join(',', keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Where(key => key.Length <= 64)
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }
}

public static class AlertBoardColumnDefaults
{
    public static List<AlertBoardColumn> Create() =>
    [
        new() { Key = "new", Title = "Новые", Subtitle = "Карточка ожидает назначения", Color = "#F0A43B", VisibleToRoles = AlertBoardRoleVisibility.For("Operator", "Technician"), SortOrder = 0 },
        new() { Key = "assigned", Title = "Назначено технику", Subtitle = "Техник должен принять задачу", Color = "#E68A00", VisibleToRoles = AlertBoardRoleVisibility.For("Technician"), SortOrder = 1 },
        new() { Key = "working", Title = "В работе", Subtitle = "Техник выполняет работы", Color = "#2F80ED", VisibleToRoles = AlertBoardRoleVisibility.For("Technician"), SortOrder = 2 },
        new() { Key = "waiting", Title = "Ожидает уточнения", Subtitle = "Нужны данные, доступ или ответ", Color = "#77858F", VisibleToRoles = AlertBoardRoleVisibility.For("Operator", "Technician"), SortOrder = 3 },
        new() { Key = "review", Title = "На проверке", Subtitle = "Техник завершил, менеджер проверяет", Color = "#00A2A2", VisibleToRoles = AlertBoardRoleVisibility.For("Operator"), SortOrder = 4 },
        new() { Key = "ready", Title = "Готово к закрытию", Subtitle = "Результат проверен менеджером", Color = "#2FA34D", VisibleToRoles = AlertBoardRoleVisibility.For("Operator"), SortOrder = 5 }
    ];
}
