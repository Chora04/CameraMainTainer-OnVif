using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace CameraMaintainer.Tests;

public sealed class CurrentStatePersistenceTests
{
    [Fact]
    public async Task UpsertLatestAsync_ReplacesPreviousCameraState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(CreateCamera());
        await db.SaveChangesAsync();
        var repository = new HealthRepository(db);

        var first = await repository.UpsertLatestAsync(new HealthRecord
        {
            CameraId = 1,
            Reachable = false,
            StatusCode = "timeout",
            ErrorMessage = "Timed out",
            CheckedAt = new DateTime(2026, 7, 31, 10, 0, 0, DateTimeKind.Utc)
        });
        var second = await repository.UpsertLatestAsync(new HealthRecord
        {
            CameraId = 1,
            Reachable = true,
            StatusCode = "ok",
            ResponseTimeMs = 25,
            CheckedAt = new DateTime(2026, 7, 31, 10, 5, 0, DateTimeKind.Utc)
        });

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.HealthRecords.CountAsync());
        var current = await repository.GetLatestAsync(1);
        Assert.NotNull(current);
        Assert.True(current.Reachable);
        Assert.Equal("ok", current.StatusCode);
        Assert.Null(current.ErrorMessage);
        Assert.Equal(25, current.ResponseTimeMs);
    }

    [Fact]
    public async Task AlertService_PersistsErrorsButNotInformationalEvents()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(CreateCamera());
        await db.SaveChangesAsync();
        var service = new AlertService(
            new AlertRepository(db),
            NullLogger<AlertService>.Instance);

        var info = await service.CreateAlertAsync(
            1,
            AlertType.ConfigurationChanged,
            AlertSeverity.Info,
            "Configuration changed");
        var error = await service.CreateAlertAsync(
            1,
            AlertType.CameraOffline,
            AlertSeverity.Warning,
            "Camera unavailable");

        Assert.Equal(-1, info.Id);
        Assert.True(error.Id > 0);
        var stored = await db.Alerts.SingleAsync();
        Assert.Equal(AlertSeverity.Warning, stored.Severity);
        Assert.Equal("Camera unavailable", stored.Title);
    }

    [Fact]
    public async Task AlertService_AssignsStableSequenceWithinEachCamera()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(CreateCamera());
        await db.SaveChangesAsync();
        var service = new AlertService(new AlertRepository(db), NullLogger<AlertService>.Instance);

        var first = await service.CreateAlertAsync(
            1, AlertType.CameraOffline, AlertSeverity.Warning, "First");
        first.AutoResolved = true;
        await db.SaveChangesAsync();
        var second = await service.CreateAlertAsync(
            1, AlertType.LensObstructed, AlertSeverity.Warning, "Second");

        Assert.Equal(1, first.CameraSequenceNumber);
        Assert.Equal(2, second.CameraSequenceNumber);
        Assert.Equal(3, (await db.Cameras.SingleAsync()).NextAlertSequence);
    }

    [Fact]
    public async Task AlertService_DoesNotDuplicateAssignedActiveError()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(CreateCamera());
        db.AppUsers.Add(CreateUser(10, "operator", "Оператор"));
        await db.SaveChangesAsync();
        var service = new AlertService(
            new AlertRepository(db),
            NullLogger<AlertService>.Instance);

        var first = await service.CreateAlertAsync(
            1,
            AlertType.LensObstructed,
            AlertSeverity.Warning,
            "Lens obstructed");
        await service.AssignAlertAsync(first.Id, 10, 10);
        var duplicate = await service.CreateAlertAsync(
            1,
            AlertType.LensObstructed,
            AlertSeverity.Warning,
            "Lens obstructed again");

        Assert.Equal(-1, duplicate.Id);
        Assert.Equal(1, await db.Alerts.CountAsync());
        Assert.False((await db.Alerts.SingleAsync()).AutoResolved);
        var audit = await db.AlertAuditEvents.ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.Single(audit, item => item.EventType == "created");
    }

    [Fact]
    public async Task AlertWorkflow_PersistsManagerReportAndPhysicalServiceNotification()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(CreateCamera());
        db.AppUsers.AddRange(
            CreateUser(10, "admin", "Администратор", canManageUsers: true),
            CreateUser(11, "manager", "Менеджер камер"));
        await db.SaveChangesAsync();
        var service = new AlertService(
            new AlertRepository(db),
            NullLogger<AlertService>.Instance);

        var alert = await service.CreateAlertAsync(
            1,
            AlertType.LensObstructed,
            AlertSeverity.Warning,
            "Lens obstructed");
        await service.AssignAlertAsync(alert.Id, 11, 10);
        await service.UpdateAlertWorkAsync(
            alert.Id,
            ErrorCategory.Physical,
            "Проверил крепление и сообщил инженерной службе.",
            serviceNotified: true,
            actorUserId: 11);
        await service.MoveAlertAsync(alert.Id, "ready", 10);
        await service.ResolveAlertAsync(alert.Id, 10);

        var stored = await db.Alerts.SingleAsync();
        Assert.Equal(ErrorCategory.Physical, stored.Category);
        Assert.Equal(11, stored.AssignedToUserId);
        Assert.Equal("Менеджер камер", stored.AssignedToDisplayName);
        Assert.Equal("admin", stored.AssignedByUsername);
        Assert.Equal("Проверил крепление и сообщил инженерной службе.", stored.WorkReport);
        Assert.True(stored.ServiceNotified);
        Assert.Equal("manager", stored.ServiceNotifiedByUsername);
        Assert.True(stored.AutoResolved);
        Assert.Equal("admin", stored.ResolvedByUsername);
        Assert.NotNull(stored.ResolvedAt);

        var audit = await db.AlertAuditEvents
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync();
        Assert.Equal(
            ["created", "assigned", "work_updated", "stage_changed", "resolved"],
            audit.Select(item => item.EventType).ToArray());
        Assert.Equal(
            ["system", "admin", "manager", "admin", "admin"],
            audit.Select(item => item.ActorUsername).ToArray());
        using var workSnapshot = JsonDocument.Parse(audit[2].NewValuesJson!);
        Assert.StartsWith(
            "Проверил крепление",
            workSnapshot.RootElement.GetProperty("workReport").GetString());
        Assert.Contains("Ответственным назначен", audit[1].Summary);
    }

    [Fact]
    public async Task AlertWorkflow_RequiresReportBeforeManualResolution()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(CreateCamera());
        db.AppUsers.Add(CreateUser(10, "operator", "Оператор"));
        await db.SaveChangesAsync();
        var service = new AlertService(
            new AlertRepository(db),
            NullLogger<AlertService>.Instance);

        var alert = await service.CreateAlertAsync(
            1,
            AlertType.AuthenticationFailed,
            AlertSeverity.Warning,
            "Invalid credentials");
        await service.AssignAlertAsync(alert.Id, 10, 10);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ResolveAlertAsync(alert.Id, 10));

        Assert.Contains("отчёт", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False((await db.Alerts.SingleAsync()).AutoResolved);
    }

    [Fact]
    public async Task AlertWorkflow_PhysicalResolutionRequiresServiceNotification()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(CreateCamera());
        db.AppUsers.AddRange(
            CreateUser(10, "operator", "Оператор"),
            CreateUser(11, "admin", "Администратор", canManageUsers: true));
        await db.SaveChangesAsync();
        var service = new AlertService(
            new AlertRepository(db),
            NullLogger<AlertService>.Instance);

        var alert = await service.CreateAlertAsync(
            1,
            AlertType.TemperatureHigh,
            AlertSeverity.Critical,
            "Camera overheated");
        await service.AssignAlertAsync(alert.Id, 10, 10);
        await service.UpdateAlertWorkAsync(
            alert.Id,
            ErrorCategory.Physical,
            "Проверил температуру корпуса.",
            serviceNotified: false,
            actorUserId: 10);
        await service.MoveAlertAsync(alert.Id, "ready", 11);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ResolveAlertAsync(alert.Id, 11));

        Assert.Contains("служба", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False((await db.Alerts.SingleAsync()).AutoResolved);
    }

    [Fact]
    public async Task AlertWorkflow_OperatorCannotAssignAnotherManager()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(CreateCamera());
        db.AppUsers.AddRange(
            CreateUser(10, "operator1", "Оператор 1"),
            CreateUser(11, "operator2", "Оператор 2"));
        await db.SaveChangesAsync();
        var service = new AlertService(
            new AlertRepository(db),
            NullLogger<AlertService>.Instance);

        var alert = await service.CreateAlertAsync(
            1,
            AlertType.CameraOffline,
            AlertSeverity.Warning,
            "Camera unavailable");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.AssignAlertAsync(alert.Id, 11, 10));
        Assert.Null((await db.Alerts.SingleAsync()).AssignedToUserId);
    }

    [Fact]
    public async Task AlertRepository_RecoveryRecordsSystemInHistoryWithoutClosing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(CreateCamera());
        db.Alerts.Add(new Alert
        {
            CameraId = 1,
            AlertType = AlertType.CameraOffline,
            Severity = AlertSeverity.Warning,
            Title = "Camera unavailable"
        });
        await db.SaveChangesAsync();
        var repository = new AlertRepository(db);

        await repository.MarkConnectivityRecoveredAsync(1);

        var stored = await db.Alerts.SingleAsync();
        Assert.False(stored.AutoResolved);
        Assert.Null(stored.ResolvedByUsername);
        Assert.Null(stored.ResolvedAt);
        Assert.NotNull(stored.RecoveryDetectedAt);
        Assert.Equal("ready", stored.WorkflowStage);
        var audit = await db.AlertAuditEvents.SingleAsync();
        Assert.Equal("recovery_detected", audit.EventType);
        Assert.Equal("system", audit.ActorUsername);
        Assert.Contains("автоматически", audit.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AlertWorkflow_RecordsEveryStageMoveWithActorAndSnapshots()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(CreateCamera());
        var actor = CreateUser(10, "operator", "Оператор");
        actor.Role = "Technician";
        db.AppUsers.Add(actor);
        await db.SaveChangesAsync();
        var service = new AlertService(
            new AlertRepository(db),
            NullLogger<AlertService>.Instance);

        var alert = await service.CreateAlertAsync(
            1,
            AlertType.CameraOffline,
            AlertSeverity.Warning,
            "Camera unavailable");
        await service.MoveAlertAsync(alert.Id, "working", 10);
        await service.MoveAlertAsync(alert.Id, "new", 10);

        var moves = await db.AlertAuditEvents
            .Where(item => item.EventType == "stage_changed")
            .OrderBy(item => item.Id)
            .ToListAsync();
        Assert.Equal(2, moves.Count);
        Assert.All(moves, item => Assert.Equal("operator", item.ActorUsername));
        Assert.Equal("new", moves[0].FromStage);
        Assert.Equal("working", moves[0].ToStage);
        Assert.Equal("working", moves[1].FromStage);
        Assert.Equal("new", moves[1].ToStage);
        Assert.NotNull(moves[0].PreviousValuesJson);
        Assert.NotNull(moves[0].NewValuesJson);
    }

    [Fact]
    public async Task AlertAudit_OperatorCanReadOnlyOwnAssignedHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(CreateCamera());
        var privilegedOperator = CreateUser(13, "operator3", "Оператор 3");
        privilegedOperator.CanManageUsers = true;
        db.AppUsers.AddRange(
            CreateUser(10, "operator1", "Оператор 1"),
            CreateUser(11, "operator2", "Оператор 2"),
            CreateUser(12, "admin", "Администратор", canManageUsers: true),
            privilegedOperator);
        await db.SaveChangesAsync();
        var service = new AlertService(
            new AlertRepository(db),
            NullLogger<AlertService>.Instance);

        var alert = await service.CreateAlertAsync(
            1,
            AlertType.CameraOffline,
            AlertSeverity.Warning,
            "Camera unavailable");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.GetAlertAuditAsync(alert.Id, 10));
        var adminHistory = await service.GetAlertAuditAsync(alert.Id, 12);
        Assert.Single(adminHistory);

        await service.AssignAlertAsync(alert.Id, 10, 12);

        var ownHistory = await service.GetAlertAuditAsync(alert.Id, 10);
        Assert.Equal(2, ownHistory.Count);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.GetAlertAuditAsync(alert.Id, 11));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.GetAlertAuditAsync(alert.Id, 13));
    }

    [Fact]
    public async Task CameraAlertHistory_IsScopedToAssigneeAndIncludesDetailedAudit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(CreateCamera());
        db.AppUsers.AddRange(
            CreateUser(10, "operator", "Оператор"),
            CreateUser(11, "technician", "Техник"),
            CreateUser(12, "admin", "Администратор", canManageUsers: true));
        await db.SaveChangesAsync();
        var service = new AlertService(
            new AlertRepository(db),
            NullLogger<AlertService>.Instance);

        var operatorAlert = await service.CreateAlertAsync(
            1,
            AlertType.CameraOffline,
            AlertSeverity.Warning,
            "Camera unavailable");
        await service.AssignAlertAsync(operatorAlert.Id, 10, 12);
        await service.RecordAlertViewedAsync(operatorAlert.Id, 10);
        await service.RecordAlertViewedAsync(operatorAlert.Id, 10);
        await service.UpdateAlertWorkAsync(operatorAlert.Id, ErrorCategory.Technical, "Connection restored", false, 10);
        await service.MoveAlertAsync(operatorAlert.Id, "ready", 10);
        await service.ResolveAlertAsync(operatorAlert.Id, 10);

        var technicianAlert = await service.CreateAlertAsync(
            1,
            AlertType.FirmwareOutdated,
            AlertSeverity.Warning,
            "Firmware outdated");
        await service.AssignAlertAsync(technicianAlert.Id, 11, 12);

        var operatorHistory = await service.GetCameraAlertHistoryAsync(1, 10);
        var technicianHistory = await service.GetCameraAlertHistoryAsync(1, 11);
        var adminHistory = await service.GetCameraAlertHistoryAsync(1, 12);

        Assert.Single(operatorHistory);
        Assert.Equal(operatorAlert.Id, operatorHistory[0].Id);
        Assert.Single(operatorHistory[0].AuditEvents, item => item.EventType == "viewed");
        Assert.Single(technicianHistory);
        Assert.DoesNotContain(technicianHistory[0].AuditEvents, item => item.EventType == "viewed");
        Assert.Equal(2, adminHistory.Count);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.RecordAlertViewedAsync(operatorAlert.Id, 11));
    }

    [Fact]
    public async Task AlertAssignment_CannotBeTakenFromAnotherOperator()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(CreateCamera());
        db.AppUsers.AddRange(
            CreateUser(10, "operator1", "Оператор 1"),
            CreateUser(11, "operator2", "Оператор 2"),
            CreateUser(12, "admin", "Администратор", canManageUsers: true));
        await db.SaveChangesAsync();
        var service = new AlertService(
            new AlertRepository(db),
            NullLogger<AlertService>.Instance);

        var alert = await service.CreateAlertAsync(
            1,
            AlertType.CameraOffline,
            AlertSeverity.Warning,
            "Camera unavailable");
        await service.AssignAlertAsync(alert.Id, 10, 12);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.AssignAlertAsync(alert.Id, 11, 11));

        var stored = await db.Alerts.AsNoTracking().SingleAsync(item => item.Id == alert.Id);
        Assert.Equal(10, stored.AssignedToUserId);
        Assert.Equal("operator1", stored.AssignedToUsername);
    }

    [Fact]
    public async Task CurrentCameraHealthMigration_KeepsLatestStateAndOnlyErrors()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260731071349_InitialCreate");

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO cameras (
                Id, Name, ServiceUrl, Port, Status, ConsecutiveFailures,
                PtzSupported, EventsSupported, Tags, Notes, AutoBackup,
                AutoRecovery, CheckIntervalSeconds, CreatedAt, UpdatedAt,
                DirectionStatus, BaselineHistogramJson
            )
            VALUES (
                1, 'Camera 1', 'http://192.0.2.1/onvif/device_service',
                80, 'Unknown', 0, 0, 0, '[]', '', 1, 1, 300,
                '2026-07-31 09:00:00', '2026-07-31 09:00:00', 'Unknown', '{{}}'
            );

            INSERT INTO health_checks (
                CameraId, Reachable, StatusCode, ResponseTimeMs, DetailJson,
                DirectionStatus, CheckedAt
            )
            VALUES
                (1, 0, 'timeout', 1000, '{{}}', 'Unknown', '2026-07-31 09:00:00'),
                (1, 1, 'ok', 20, '{{}}', 'Normal', '2026-07-31 10:00:00');

            INSERT INTO alerts (
                CameraId, Severity, AlertType, Title, AutoResolved, CreatedAt
            )
            VALUES
                (1, 'Info', 'ConfigurationChanged', 'Information', 0, '2026-07-31 09:00:00'),
                (1, 'Warning', 'CameraOffline', 'Error', 1, '2026-07-31 10:00:00'),
                (1, 'Warning', 'CameraOffline', 'Error', 0, '2026-07-31 11:00:00');
            """);

        await migrator.MigrateAsync();

        var health = await db.HealthRecords.SingleAsync();
        Assert.True(health.Reachable);
        Assert.Equal("ok", health.StatusCode);
        var alert = await db.Alerts.SingleAsync();
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
        Assert.Equal(ErrorCategory.Technical, alert.Category);
        Assert.Equal(alert.CreatedAt, alert.UpdatedAt);
        var importedAudit = await db.AlertAuditEvents.SingleAsync();
        Assert.Equal("legacy_imported", importedAudit.EventType);
        Assert.Equal(alert.Id, importedAudit.AlertId);
    }

    private static AppDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        return new AppDbContext(options);
    }

    private static Camera CreateCamera()
    {
        return new Camera
        {
            Id = 1,
            Name = "Camera 1",
            ServiceUrl = "http://192.0.2.1/onvif/device_service"
        };
    }

    private static AppUser CreateUser(
        int id,
        string username,
        string displayName,
        bool canManageUsers = false)
    {
        return new AppUser
        {
            Id = id,
            Username = username,
            DisplayName = displayName,
            Role = canManageUsers ? "Admin" : "Operator",
            CanViewCameras = true,
            CanRunChecks = true,
            CanManageCameras = true,
            CanManageUsers = canManageUsers,
            IsActive = true
        };
    }
}
