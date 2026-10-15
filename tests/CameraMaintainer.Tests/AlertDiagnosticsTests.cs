using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Services;
using CameraMaintainer.Web.Localization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CameraMaintainer.Tests;

public sealed class AlertDiagnosticsTests
{
    [Fact]
    public async Task CreateAlertAsync_PersistsCameraCheckDiagnosticOnIssue()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(new Camera { Id = 1, Name = "1", ServiceUrl = "http://192.168.4.31/onvif/device_service" });
        await db.SaveChangesAsync();
        var service = new AlertService(new AlertRepository(db), NullLogger<AlertService>.Instance);

        await service.CreateAlertAsync(
            1,
            AlertType.CameraOffline,
            AlertSeverity.Warning,
            "Ошибка проверки камеры 1",
            "timeout: A task was canceled.",
            new AlertDiagnostic
            {
                FailureCode = "timeout",
                FailureStage = "onvif_device_info",
                DurationMs = 10014,
                TimeoutMs = 10000
            });

        var issue = await db.Set<AlertIssue>().AsNoTracking().SingleAsync();
        Assert.Equal("timeout", issue.FailureCode);
        Assert.Equal("onvif_device_info", issue.FailureStage);
        Assert.Equal(10014, issue.FailureDurationMs);
        Assert.Equal(10000, issue.FailureTimeoutMs);
    }

    [Fact]
    public async Task DuplicateAlert_BackfillsMissingDiagnosticWithoutAddingAnotherIssue()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(new Camera { Id = 1, Name = "1", ServiceUrl = "http://192.168.4.31/onvif/device_service" });
        await db.SaveChangesAsync();
        var service = new AlertService(new AlertRepository(db), NullLogger<AlertService>.Instance);

        await service.CreateAlertAsync(
            1, AlertType.CameraOffline, AlertSeverity.Warning,
            "Camera check failed", "timeout: A task was canceled.");
        await service.CreateAlertAsync(
            1, AlertType.CameraOffline, AlertSeverity.Warning,
            "Camera check failed", "connection_refused: refused",
            new AlertDiagnostic
            {
                FailureCode = "connection_refused",
                FailureStage = "onvif_device_info",
                DurationMs = 2152
            });

        var alerts = await db.Alerts.AsNoTracking().ToListAsync();
        var issues = await db.Set<AlertIssue>().AsNoTracking().ToListAsync();
        Assert.Single(alerts);
        var issue = Assert.Single(issues);
        Assert.Equal("connection_refused", issue.FailureCode);
        Assert.Equal("onvif_device_info", issue.FailureStage);
        Assert.Equal(2152, issue.FailureDurationMs);
    }

    [Fact]
    public void GenericTimeoutTitle_BecomesHumanReadable()
    {
        var title = CameraErrorMessages.Title(
            AlertType.CameraOffline,
            "Ошибка проверки камеры 1",
            "timeout: A task was canceled.");

        Assert.Equal("Камера не ответила вовремя", title);
    }

    private static AppDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        return new AppDbContext(options);
    }
}
