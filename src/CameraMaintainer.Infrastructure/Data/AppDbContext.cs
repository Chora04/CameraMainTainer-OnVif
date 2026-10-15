using CameraMaintainer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CameraMaintainer.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<Credential> Credentials => Set<Credential>();
    public DbSet<HealthRecord> HealthRecords => Set<HealthRecord>();
    public DbSet<PtzPreset> PtzPresets => Set<PtzPreset>();
    public DbSet<ConfigBackup> ConfigBackups => Set<ConfigBackup>();
    public DbSet<MaintenanceJob> MaintenanceJobs => Set<MaintenanceJob>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AlertIssue> AlertIssues => Set<AlertIssue>();
    public DbSet<AlertIssueMessage> AlertIssueMessages => Set<AlertIssueMessage>();
    public DbSet<AlertAuditEvent> AlertAuditEvents => Set<AlertAuditEvent>();
    public DbSet<AlertHandoff> AlertHandoffs => Set<AlertHandoff>();
    public DbSet<AlertHandoffReason> AlertHandoffReasons => Set<AlertHandoffReason>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<WorkspaceNote> WorkspaceNotes => Set<WorkspaceNote>();
    public DbSet<ScanRecord> ScanRecords => Set<ScanRecord>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<FirmwareRecord> FirmwareRecords => Set<FirmwareRecord>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<CameraFrameRecord> CameraFrameRecords => Set<CameraFrameRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Camera
        modelBuilder.Entity<Camera>(entity =>
        {
            entity.ToTable("cameras");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.IpAddress);
            entity.HasIndex(e => e.LastCheckAt);
            entity.HasIndex(e => new { e.AutomaticChecksEnabled, e.CheckQueuePosition });
            entity.HasIndex(e => e.SerialNumber).IsUnique();

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.Tags).HasMaxLength(4000);
            entity.Property(e => e.Notes).HasMaxLength(4000);
            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.ServiceUrl).HasMaxLength(512);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.StreetAddress).HasMaxLength(600);
            entity.Property(e => e.AutomaticChecksEnabled).HasDefaultValue(true);
            entity.Property(e => e.NextAlertSequence).HasDefaultValue(1);

            // Direction analysis columns
            entity.Property(e => e.DirectionStatus)
                .HasMaxLength(30)
                .HasDefaultValue("Unknown");
            entity.Property(e => e.BaselineHistogramJson)
                .HasMaxLength(4000)
                .HasDefaultValue("{}");
        });

        // Credential
        modelBuilder.Entity<Credential>(entity =>
        {
            entity.ToTable("credentials");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CameraId).IsUnique();
            entity.HasOne(e => e.Camera)
                .WithOne(c => c.Credential)
                .HasForeignKey<Credential>(e => e.CameraId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // HealthRecord
        modelBuilder.Entity<HealthRecord>(entity =>
        {
            entity.ToTable("health_checks");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CameraId).IsUnique();
            entity.HasOne(e => e.Camera)
                .WithOne(c => c.CurrentHealth)
                .HasForeignKey<HealthRecord>(e => e.CameraId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CameraFrameRecord>(entity =>
        {
            entity.ToTable("camera_frame_records");
            entity.HasKey(record => record.Id);
            entity.HasIndex(record => new { record.CameraId, record.CapturedAt })
                .IsDescending(false, true);
            entity.Property(record => record.TriggerCodes).HasMaxLength(512);
            entity.Property(record => record.DirectionStatus).HasMaxLength(30);
            entity.Property(record => record.ObstructionType).HasMaxLength(40);
            entity.Property(record => record.ImageRelativePath).HasMaxLength(600);
            entity.Property(record => record.ImageContentType).HasMaxLength(80);
            entity.HasOne(record => record.Camera)
                .WithMany(camera => camera.FrameRecords)
                .HasForeignKey(record => record.CameraId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // PtzPreset
        modelBuilder.Entity<PtzPreset>(entity =>
        {
            entity.ToTable("ptz_presets");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CameraId);
            entity.HasIndex(e => new { e.CameraId, e.ProfileToken, e.PresetToken }).IsUnique();
            entity.HasOne(e => e.Camera)
                .WithMany(c => c.PtzPresets)
                .HasForeignKey(e => e.CameraId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ConfigBackup
        modelBuilder.Entity<ConfigBackup>(entity =>
        {
            entity.ToTable("config_backups");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.CameraId, e.CreatedAt }).IsDescending(false, true);
            entity.HasOne(e => e.Camera)
                .WithMany(c => c.ConfigBackups)
                .HasForeignKey(e => e.CameraId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // MaintenanceJob
        modelBuilder.Entity<MaintenanceJob>(entity =>
        {
            entity.ToTable("maintenance_log");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt).IsDescending();

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);
        });

        // Alert
        modelBuilder.Entity<Alert>(entity =>
        {
            entity.ToTable("alerts");
            entity.HasQueryFilter(alert => alert.MergedIntoAlertId == null);
            entity.HasOne<Alert>().WithMany().HasForeignKey(alert => alert.MergedIntoAlertId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CreatedAt).IsDescending();
            entity.HasIndex(e => e.Severity);
            entity.HasIndex(e => e.AutoResolved);
            entity.HasIndex(e => e.WorkflowStage);
            entity.HasIndex(e => new { e.CameraId, e.CameraSequenceNumber }).IsUnique();

            entity.Property(e => e.Severity)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.AlertType)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(e => e.Category)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.Title).HasMaxLength(512);
            entity.Property(e => e.Description).HasMaxLength(4000);
            entity.Property(e => e.AssignedToUsername).HasMaxLength(64);
            entity.Property(e => e.AssignedToDisplayName).HasMaxLength(128);
            entity.Property(e => e.AssignedByUsername).HasMaxLength(64);
            entity.Property(e => e.WorkReport).HasMaxLength(4000);
            entity.Property(e => e.WorkflowStage).HasMaxLength(64);
            entity.Property(e => e.ServiceNotifiedByUsername).HasMaxLength(64);
            entity.Property(e => e.ResolvedByUsername).HasMaxLength(64);
            entity.Property(e => e.ResolvedByDisplayName).HasMaxLength(128);

            entity.HasOne(e => e.Camera)
                .WithMany(c => c.Alerts)
                .HasForeignKey(e => e.CameraId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AlertIssue>(entity =>
        {
            entity.ToTable("alert_issues");
            entity.HasQueryFilter(issue => issue.Alert!.MergedIntoAlertId == null);
            entity.HasKey(issue => issue.Id);
            entity.HasIndex(issue => new { issue.AlertId, issue.AlertType });
            entity.Property(issue => issue.AlertType).HasConversion<string>().HasMaxLength(50);
            entity.Property(issue => issue.Category).HasConversion<string>().HasMaxLength(20);
            entity.Property(issue => issue.Severity).HasConversion<string>().HasMaxLength(20);
            entity.Property(issue => issue.Title).HasMaxLength(512);
            entity.Property(issue => issue.Description).HasMaxLength(4000);
            entity.Property(issue => issue.FailureCode).HasMaxLength(64);
            entity.Property(issue => issue.FailureStage).HasMaxLength(64);
            entity.Property(issue => issue.ResolvedByUsername).HasMaxLength(64);
            entity.Property(issue => issue.ResolvedByDisplayName).HasMaxLength(128);
            entity.HasOne(issue => issue.Alert).WithMany(alert => alert.Issues)
                .HasForeignKey(issue => issue.AlertId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AlertIssueMessage>(entity =>
        {
            entity.ToTable("alert_issue_messages");
            entity.HasQueryFilter(message => message.AlertIssue!.Alert!.MergedIntoAlertId == null);
            entity.HasKey(message => message.Id);
            entity.HasIndex(message => new { message.AlertIssueId, message.CreatedAt });
            entity.Property(message => message.AuthorUsername).HasMaxLength(64);
            entity.Property(message => message.AuthorDisplayName).HasMaxLength(128);
            entity.Property(message => message.Message).HasMaxLength(4000);
            entity.HasOne(message => message.AlertIssue).WithMany(issue => issue.Messages)
                .HasForeignKey(message => message.AlertIssueId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AlertAuditEvent>(entity =>
        {
            entity.ToTable("alert_audit_events");
            entity.HasQueryFilter(item => item.Alert!.MergedIntoAlertId == null);
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.AlertId, e.CreatedAt }).IsDescending(false, true);
            entity.HasIndex(e => e.ActorUserId);
            entity.Property(e => e.EventType).HasMaxLength(40);
            entity.Property(e => e.ActorUsername).HasMaxLength(64);
            entity.Property(e => e.ActorDisplayName).HasMaxLength(128);
            entity.Property(e => e.Summary).HasMaxLength(1000);
            entity.Property(e => e.FromStage).HasMaxLength(64);
            entity.Property(e => e.ToStage).HasMaxLength(64);
            entity.Property(e => e.PreviousValuesJson).HasMaxLength(16000);
            entity.Property(e => e.NewValuesJson).HasMaxLength(16000);
            entity.HasOne(e => e.Alert)
                .WithMany(a => a.AuditEvents)
                .HasForeignKey(e => e.AlertId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AlertHandoffReason>(entity =>
        {
            entity.ToTable("alert_handoff_reasons");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Key).IsUnique();
            entity.HasIndex(e => new { e.IsActive, e.SortOrder });
            entity.Property(e => e.Key).HasMaxLength(64);
            entity.Property(e => e.Name).HasMaxLength(128);
        });

        modelBuilder.Entity<AlertHandoff>(entity =>
        {
            entity.ToTable("alert_handoffs");
            entity.HasQueryFilter(item => item.Alert!.MergedIntoAlertId == null);
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.AlertId, e.CreatedAt });
            entity.HasIndex(e => new { e.ToUserId, e.State, e.DueAt });
            entity.HasIndex(e => e.ParentHandoffId);
            entity.Property(e => e.ReasonName).HasMaxLength(128);
            entity.Property(e => e.FromUsername).HasMaxLength(64);
            entity.Property(e => e.FromDisplayName).HasMaxLength(128);
            entity.Property(e => e.ToUsername).HasMaxLength(64);
            entity.Property(e => e.ToDisplayName).HasMaxLength(128);
            entity.Property(e => e.Comment).HasMaxLength(2000);
            entity.Property(e => e.AcceptanceComment).HasMaxLength(2000);
            entity.Property(e => e.CompletionComment).HasMaxLength(2000);
            entity.Property(e => e.State).HasMaxLength(24);
            entity.HasOne(e => e.Alert)
                .WithMany(a => a.Handoffs)
                .HasForeignKey(e => e.AlertId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Reason)
                .WithMany(reason => reason.Handoffs)
                .HasForeignKey(e => e.ReasonId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ParentHandoff)
                .WithMany(parent => parent.Children)
                .HasForeignKey(e => e.ParentHandoffId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserNotification>(entity =>
        {
            entity.ToTable("user_notifications");
            entity.HasQueryFilter(item => item.Alert!.MergedIntoAlertId == null);
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.ResolvedAt, e.CreatedAt });
            entity.HasIndex(e => e.HandoffId);
            entity.Property(e => e.Type).HasMaxLength(40);
            entity.Property(e => e.Title).HasMaxLength(256);
            entity.Property(e => e.Message).HasMaxLength(1000);
            entity.Property(e => e.Tone).HasMaxLength(32);
            entity.Property(e => e.Url).HasMaxLength(512);
            entity.HasOne(e => e.Alert)
                .WithMany(a => a.Notifications)
                .HasForeignKey(e => e.AlertId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Handoff)
                .WithMany(handoff => handoff.Notifications)
                .HasForeignKey(e => e.HandoffId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WorkspaceNote>(entity =>
        {
            entity.ToTable("workspace_notes");
            entity.HasKey(note => note.Id);
            entity.HasIndex(note => new { note.CompletedAt, note.ScheduledFor });
            entity.HasIndex(note => new { note.CreatedByUserId, note.ScheduledFor });
            entity.HasIndex(note => new { note.IsVisibleToAll, note.ScheduledFor });
            entity.HasIndex(note => new { note.RecipientUserId, note.ScheduledFor });
            entity.Property(note => note.Title).HasMaxLength(256);
            entity.Property(note => note.Body).HasMaxLength(4000);
            entity.Property(note => note.Tone).HasMaxLength(20);
            entity.Property(note => note.CreatedByUsername).HasMaxLength(64);
            entity.Property(note => note.CreatedByDisplayName).HasMaxLength(128);
            entity.Property(note => note.IsVisibleToAll).HasDefaultValue(false);
            entity.Property(note => note.RecipientUsername).HasMaxLength(64);
            entity.Property(note => note.RecipientDisplayName).HasMaxLength(128);
            entity.Property(note => note.CompletedByUsername).HasMaxLength(64);
            entity.Property(note => note.CompletedByDisplayName).HasMaxLength(128);
        });

        // ScanRecord
        modelBuilder.Entity<ScanRecord>(entity =>
        {
            entity.ToTable("scan_records");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CreatedAt).IsDescending();
        });

        // Setting
        modelBuilder.Entity<Setting>(entity =>
        {
            entity.ToTable("settings");
            entity.HasKey(e => e.Key);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("app_users");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.Username).HasMaxLength(64);
            entity.Property(e => e.DisplayName).HasMaxLength(128);
            entity.Property(e => e.Role).HasMaxLength(32);
            entity.Property(e => e.PasswordHash).HasMaxLength(128);
            entity.Property(e => e.PasswordSalt).HasMaxLength(64);
            entity.Property(e => e.AlertBoardColumnKeys).HasMaxLength(4096);
            entity.Property(e => e.CanViewDashboard).HasDefaultValue(true);
            entity.Property(e => e.CanViewAlerts).HasDefaultValue(true);
            entity.Property(e => e.CanViewAlertHistory).HasDefaultValue(true);
            entity.Property(e => e.CanViewMaintenance).HasDefaultValue(true);
        });

        // FirmwareRecord
        modelBuilder.Entity<FirmwareRecord>(entity =>
        {
            entity.ToTable("firmware_records");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.CameraId, e.DetectedAt }).IsDescending(false, true);
            entity.HasIndex(e => e.CameraId);
            entity.Property(e => e.FirmwareVersion).HasMaxLength(128);
            entity.Property(e => e.HardwareId).HasMaxLength(128);
            entity.Property(e => e.Manufacturer).HasMaxLength(128);
            entity.Property(e => e.Model).HasMaxLength(128);
            entity.HasOne(e => e.Camera)
                .WithMany()
                .HasForeignKey(e => e.CameraId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
