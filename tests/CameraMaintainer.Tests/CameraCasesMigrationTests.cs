using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CameraMaintainer.Tests;

public sealed class CameraCasesMigrationTests
{
    [Fact]
    public async Task Migration_MergesOpenCardsAndPreservesHistoryReportsHandoffsAndNotifications()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260916101817_AlertRecoveryDetection");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO cameras (Id, Name, ServiceUrl, Port, Status, ConsecutiveFailures,
                PtzSupported, EventsSupported, Tags, Notes, AutoBackup, AutoRecovery, CheckIntervalSeconds, CreatedAt, UpdatedAt)
            VALUES (1, 'One', '', 80, 'Online', 0, 0, 0, '[]', '', 0, 0, 3600, '2026-09-01', '2026-09-01'),
                (2, 'Two', '', 80, 'Online', 0, 0, 0, '[]', '', 0, 0, 3600, '2026-09-01', '2026-09-01');
            INSERT INTO alerts (Id, CameraId, Severity, AlertType, Category, Title, AutoResolved, CreatedAt,
                RecoveryDetectedAt, ResolvedAt, WorkflowStage, AssignedToUserId, AssignedToDisplayName, WorkReport)
            VALUES (1, 1, 'Warning', 'LensObstructed', 'Physical', 'Lens', 0, '2026-09-01', '2026-09-03', NULL, 'ready', 10, 'First owner', 'Original report'),
                (2, 1, 'Critical', 'CameraOffline', 'Technical', 'Offline', 0, '2026-09-02', NULL, NULL, 'working', 11, 'Second owner', 'Second report'),
                (3, 1, 'Warning', 'CameraOffline', 'Technical', 'Closed', 1, '2026-08-01', NULL, '2026-08-02', 'ready', NULL, NULL, NULL),
                (4, 2, 'Warning', 'CameraOffline', 'Technical', 'Other camera', 0, '2026-09-02', NULL, NULL, 'new', NULL, NULL, NULL);
            INSERT INTO alert_audit_events (AlertId, EventType, ActorUsername, ActorDisplayName, Summary, CreatedAt)
            VALUES (2, 'work_updated', 'second', 'Second owner', 'Work recorded', '2026-09-02');
            INSERT INTO alert_handoffs (Id, AlertId, ReasonId, ReasonName, FromUserId, FromUsername, FromDisplayName,
                ToUserId, ToUsername, ToDisplayName, Comment, State, CreatedAt, DueAt)
            VALUES (1, 2, 1, 'Need parts', 11, 'second', 'Second owner', 12, 'recipient', 'Recipient', 'Order part', 'Pending', '2026-09-02', '2026-09-10');
            INSERT INTO user_notifications (UserId, AlertId, HandoffId, Type, Title, Message, Tone, Url, CreatedAt, ReminderCount)
            VALUES (12, 2, 1, 'handoff', 'Assigned', 'Order part', 'info', '/Alerts?view=active&alert=2', '2026-09-02', 0);
            """);

        await migrator.MigrateAsync();
        var root = await db.Alerts.Include(item => item.Issues).SingleAsync(item => item.Id == 1);
        Assert.Equal(3, await db.Alerts.CountAsync());
        Assert.Equal(4, await db.Alerts.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, root.Issues.Count);
        Assert.Single(root.Issues, issue => issue.RecoveredAt == null);
        Assert.Null(root.RecoveryDetectedAt);
        Assert.Equal("Offline", root.Title);
        Assert.Equal("working", root.WorkflowStage);
        Assert.Equal(10, root.AssignedToUserId);
        Assert.Equal("Original report", root.WorkReport);
        var source = await db.Alerts.IgnoreQueryFilters().SingleAsync(item => item.Id == 2);
        Assert.Equal(1, source.MergedIntoAlertId);
        Assert.Equal("Second report", source.WorkReport);
        var audit = await db.AlertAuditEvents.OrderBy(item => item.Id).ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, item => Assert.Equal(1, item.AlertId));
        Assert.Contains("Second report", audit.Single(item => item.EventType == "case_merged").PreviousValuesJson!);
        Assert.Equal(1, (await db.AlertHandoffs.SingleAsync()).AlertId);
        var notification = await db.UserNotifications.SingleAsync();
        Assert.Equal(1, notification.AlertId);
        Assert.EndsWith("alert=1", notification.Url);
        Assert.Equal(new DateTime(2026, 8, 2), (await db.Set<AlertIssue>().SingleAsync(item => item.AlertId == 3)).RecoveredAt);
        Assert.All(await db.Cameras.ToListAsync(), camera => Assert.Equal(0, camera.ConsecutiveHealthyChecks));

        // Retention must remove hidden source rows along with the canonical card.
        db.ChangeTracker.Clear();
        await db.Alerts.Where(item => item.Id == 1).ExecuteDeleteAsync();
        Assert.Equal(2, await db.Alerts.IgnoreQueryFilters().CountAsync());
        Assert.Empty(await db.AlertAuditEvents.ToListAsync());
        Assert.Empty(await db.AlertHandoffs.ToListAsync());
        Assert.Empty(await db.UserNotifications.ToListAsync());
        Assert.Equal(2, await db.Set<AlertIssue>().CountAsync());
    }
}
