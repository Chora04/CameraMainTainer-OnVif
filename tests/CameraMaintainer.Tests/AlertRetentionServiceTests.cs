using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CameraMaintainer.Tests;

public sealed class AlertRetentionServiceTests
{
    [Fact]
    public async Task CleanupAsync_RemovesOnlyResolvedAlertsOutsideCalendarWindow()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();

        var now = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        db.Alerts.AddRange(
            ResolvedAlert(1, new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc)),
            ResolvedAlert(2, new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc)),
            ResolvedAlert(3, new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc)),
            new Alert
            {
                Id = 4,
                Title = "Active old alert",
                AutoResolved = false,
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        db.AlertAuditEvents.AddRange(
            AuditEvent(1, 1),
            AuditEvent(2, 2),
            AuditEvent(3, 3),
            AuditEvent(4, 4));
        await db.SaveChangesAsync();

        var service = new AlertRetentionService(db);
        await service.SavePolicyAsync(3);
        var result = await service.CleanupAsync(now);

        Assert.Equal(new DateTime(2026, 8, 26, 19, 0, 0, DateTimeKind.Utc), result.CutoffUtc);
        Assert.Equal(1, result.DeletedAlerts);
        Assert.Equal(1, result.DeletedAuditEvents);
        var remainingAlertIds = await db.Alerts
            .OrderBy(alert => alert.Id)
            .Select(alert => alert.Id)
            .ToArrayAsync();
        var remainingAuditAlertIds = await db.AlertAuditEvents
            .OrderBy(audit => audit.AlertId)
            .Select(audit => audit.AlertId)
            .ToArrayAsync();
        Assert.Equal(new[] { 2, 3, 4 }, remainingAlertIds);
        Assert.Equal(new[] { 2, 3, 4 }, remainingAuditAlertIds);
    }

    [Fact]
    public async Task CleanupAsync_UnlimitedRetentionKeepsHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Alerts.Add(ResolvedAlert(1, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var service = new AlertRetentionService(db);
        await service.SavePolicyAsync(0);
        var result = await service.CleanupAsync(
            new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc));

        Assert.Null(result.CutoffUtc);
        Assert.Equal(0, result.DeletedAlerts);
        Assert.Equal(1, await db.Alerts.CountAsync());
    }

    private static Alert ResolvedAlert(int id, DateTime resolvedAt)
    {
        return new Alert
        {
            Id = id,
            Title = $"Resolved alert {id}",
            AutoResolved = true,
            ResolvedAt = resolvedAt,
            CreatedAt = resolvedAt.AddHours(-1),
            UpdatedAt = resolvedAt
        };
    }

    private static AlertAuditEvent AuditEvent(long id, int alertId)
    {
        return new AlertAuditEvent
        {
            Id = id,
            AlertId = alertId,
            EventType = "resolved",
            Summary = "Resolved",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    private static AppDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        return new AppDbContext(options);
    }
}
