namespace CameraMaintainer.Core.Models;

public sealed class WorkspaceNote
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string Tone { get; set; } = "info";
    public DateTime ScheduledFor { get; set; }
    public int CreatedByUserId { get; set; }
    public string CreatedByUsername { get; set; } = string.Empty;
    public string CreatedByDisplayName { get; set; } = string.Empty;
    public bool IsVisibleToAll { get; set; }
    public int? RecipientUserId { get; set; }
    public string? RecipientUsername { get; set; }
    public string? RecipientDisplayName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int? CompletedByUserId { get; set; }
    public string? CompletedByUsername { get; set; }
    public string? CompletedByDisplayName { get; set; }
}
