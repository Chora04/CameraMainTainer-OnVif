using System.Text.Json.Serialization;

namespace CameraMaintainer.Core.Models;

public static class AlertHandoffStates
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Forwarded = "forwarded";
    public const string Completed = "completed";
    public const string Rejected = "rejected";

    public static bool IsOpen(string? state) =>
        string.Equals(state, Pending, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, Accepted, StringComparison.OrdinalIgnoreCase);
}

public sealed class AlertHandoffReason
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public ICollection<AlertHandoff> Handoffs { get; set; } = new List<AlertHandoff>();
}

public sealed class AlertHandoff
{
    public long Id { get; set; }
    public int AlertId { get; set; }
    public long? ParentHandoffId { get; set; }
    public int ReasonId { get; set; }
    public string ReasonName { get; set; } = string.Empty;
    public int FromUserId { get; set; }
    public string FromUsername { get; set; } = string.Empty;
    public string FromDisplayName { get; set; } = string.Empty;
    public int ToUserId { get; set; }
    public string ToUsername { get; set; } = string.Empty;
    public string ToDisplayName { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public string? AcceptanceComment { get; set; }
    public string? CompletionComment { get; set; }
    public string State { get; set; } = AlertHandoffStates.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DueAt { get; set; } = DateTime.UtcNow.AddHours(24);
    public DateTime? ViewedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    [JsonIgnore]
    public Alert? Alert { get; set; }

    [JsonIgnore]
    public AlertHandoffReason? Reason { get; set; }

    [JsonIgnore]
    public AlertHandoff? ParentHandoff { get; set; }

    [JsonIgnore]
    public ICollection<AlertHandoff> Children { get; set; } = new List<AlertHandoff>();

    [JsonIgnore]
    public ICollection<UserNotification> Notifications { get; set; } = new List<UserNotification>();
}

public sealed class UserNotification
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public int AlertId { get; set; }
    public long? HandoffId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Tone { get; set; } = "assignment";
    public string Url { get; set; } = "/Alerts?view=active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ReminderAt { get; set; }
    public DateTime? LastRemindedAt { get; set; }
    public int ReminderCount { get; set; }

    [JsonIgnore]
    public Alert? Alert { get; set; }

    [JsonIgnore]
    public AlertHandoff? Handoff { get; set; }
}
