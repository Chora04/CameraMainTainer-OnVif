using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CameraMaintainer.Tests;

public sealed class AlertRecoveryTests
{
    [Fact]
    public async Task DifferentFaults_AppendToOpenCardWithoutChangingOwnerOrStart()
    {
        await using var f = await Fixture.CreateAsync();
        var first = await f.Service.CreateAlertAsync(1, AlertType.LensObstructed, AlertSeverity.Warning, "Lens", "Covered");
        await f.Service.AssignAlertAsync(first.Id, 10, 10);
        var startedAt = first.CreatedAt;
        var second = await f.Service.CreateAlertAsync(1, AlertType.CameraOffline, AlertSeverity.Critical, "Offline", "Timeout");
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(startedAt, second.CreatedAt);
        Assert.Equal(10, second.AssignedToUserId);
        Assert.Equal(2, second.Issues.Count);
        Assert.Contains(second.Issues, issue => issue.Title == "Lens" && issue.Description == "Covered");
        Assert.Contains(second.Issues, issue => issue.Title == "Offline" && issue.Description == "Timeout");
        await f.Service.CreateAlertAsync(1, AlertType.CameraOffline, AlertSeverity.Critical, "Offline", "Timeout");
        Assert.Equal(2, second.Issues.Count);
        Assert.Single(await f.Db.AlertAuditEvents.Where(item => item.EventType == "issue_added").ToListAsync());
        Assert.Single(await f.Db.Alerts.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentFaultTypes_UseSingleCardAcrossContexts()
    {
        var connectionString = $"Data Source=case-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(new Camera { Id = 1, Name = "One" });
        await db.SaveChangesAsync();
        async Task<int> Create(AlertType type)
        {
            await using var context = new AppDbContext(options);
            var service = new AlertService(new AlertRepository(context), NullLogger<AlertService>.Instance);
            return (await service.CreateAlertAsync(1, type, AlertSeverity.Warning, type.ToString())).Id;
        }
        var ids = await Task.WhenAll(
            Task.Run(() => Create(AlertType.LensObstructed)),
            Task.Run(() => Create(AlertType.CameraOffline)));
        Assert.Equal(ids[0], ids[1]);
        Assert.Single(await db.Alerts.ToListAsync());
        Assert.Equal(2, await db.Set<AlertIssue>().CountAsync());
    }

    [Theory]
    [InlineData(DirectionStatus.Normal, false, null, true)]
    [InlineData(DirectionStatus.Normal, false, "Download failed", false)]
    [InlineData(DirectionStatus.Unknown, false, null, false)]
    [InlineData(DirectionStatus.Unknown, false, "Decode failed", false)]
    [InlineData(DirectionStatus.WallObstructed, false, null, false)]
    [InlineData(DirectionStatus.Normal, true, null, false)]
    public void LensRecovery_RequiresPositiveNormalAnalysis(DirectionStatus status, bool obstructed, string? error, bool expected)
    {
        var result = new LensObstructionDetector.ObstructionResult(obstructed, "none", status,
            110, 40, 7, 35, 20, 0.9, null, error, 100, 5, 30000);
        Assert.Equal(expected, result.ConfirmsRecovery);
    }

    [Fact]
    public async Task Recovery_DoesNotGiveTechnicianPermissionToClose()
    {
        await using var f = await Fixture.CreateAsync();
        var actor = (await f.Db.AppUsers.FindAsync(10))!;
        actor.Role = "Technician";
        await f.Db.SaveChangesAsync();
        var alert = await f.Service.CreateAlertAsync(1, AlertType.LensObstructed, AlertSeverity.Warning, "Obstructed");
        await f.Service.AssignAlertAsync(alert.Id, 10, 10);
        await f.Service.UpdateAlertWorkAsync(alert.Id, ErrorCategory.Physical, "Report", false, 10);
        await f.Repository.MarkRecoveredAsync(1, AlertType.LensObstructed);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.ResolveAlertAsync(alert.Id, 10));
        Assert.False(alert.AutoResolved);
    }

    [Fact]
    public async Task Recovery_IsIdempotentPreservesWorkAndOnlyChangesConfirmedFault()
    {
        await using var f = await Fixture.CreateAsync();
        var alert = await f.Service.CreateAlertAsync(1, AlertType.LensObstructed, AlertSeverity.Warning, "Obstructed");
        await f.Service.AssignAlertAsync(alert.Id, 10, 10);
        await f.Service.UpdateAlertWorkAsync(alert.Id, ErrorCategory.Physical, "Waiting for access", false, 10);
        var otherType = await f.Service.CreateAlertAsync(1, AlertType.ConfigurationChanged, AlertSeverity.Warning, "Clock drift");
        var otherCamera = await f.Service.CreateAlertAsync(2, AlertType.LensObstructed, AlertSeverity.Warning, "Obstructed");
        var lastCheck = (await f.Db.Cameras.FindAsync(1))!.LastCheckAt;

        await f.Repository.MarkRecoveredAsync(1, AlertType.LensObstructed);
        var recoveredAt = alert.Issues.Single(issue => issue.AlertType == AlertType.LensObstructed).RecoveredAt;
        var updatedAt = alert.UpdatedAt;
        await f.Repository.MarkRecoveredAsync(1, AlertType.LensObstructed);

        Assert.False(alert.AutoResolved);
        Assert.Null(alert.ResolvedAt);
        Assert.NotNull(recoveredAt);
        Assert.Equal(recoveredAt, alert.Issues.Single(issue => issue.AlertType == AlertType.LensObstructed).RecoveredAt);
        Assert.Null(alert.RecoveryDetectedAt);
        Assert.Equal(updatedAt, alert.UpdatedAt);
        Assert.Equal("working", alert.WorkflowStage);
        Assert.Equal("Waiting for access", alert.WorkReport);
        Assert.Equal(10, alert.AssignedToUserId);
        Assert.False(alert.ServiceNotified);
        Assert.Null(otherType.RecoveryDetectedAt);
        Assert.Null(otherCamera.RecoveryDetectedAt);
        Assert.Equal(lastCheck, (await f.Db.Cameras.FindAsync(1))!.LastCheckAt);
        var partialAudit = Assert.Single(await f.Db.AlertAuditEvents.Where(item => item.EventType == "issue_recovered").ToListAsync());
        Assert.Equal("working", partialAudit.ToStage);
        await f.Repository.MarkRecoveredAsync(1, AlertType.ConfigurationChanged);
        Assert.Equal("ready", alert.WorkflowStage);
        var audit = Assert.Single(await f.Db.AlertAuditEvents.Where(item => item.EventType == "recovery_detected").ToListAsync());
        Assert.Equal("working", audit.FromStage);
        Assert.Equal("ready", audit.ToStage);
        Assert.Contains("recoveryDetectedAt", audit.NewValuesJson!);
        Assert.False((await new CameraRepository(f.Db).GetCheckQueueAsync()).Single(item => item.CameraId == 1).HasPhysicalFault);
    }

    [Theory]
    [InlineData(false, "new")]
    [InlineData(true, "working")]
    public async Task Recurrence_ReusesCardAndReturnsToWorkWithoutDuplicate(bool assigned, string expectedStage)
    {
        await using var f = await Fixture.CreateAsync();
        var alert = await f.Service.CreateAlertAsync(1, AlertType.LensObstructed, AlertSeverity.Warning, "First fault");
        if (assigned) await f.Service.AssignAlertAsync(alert.Id, 10, 10);
        var startedAt = alert.CreatedAt;
        await f.Repository.MarkRecoveredAsync(1, AlertType.LensObstructed);

        var reopened = await f.Service.CreateAlertAsync(1, AlertType.LensObstructed, AlertSeverity.Critical, "Fault returned", "New evidence");
        await f.Service.CreateAlertAsync(1, AlertType.LensObstructed, AlertSeverity.Critical, "Fault returned");

        Assert.Equal(alert.Id, reopened.Id);
        Assert.Equal(startedAt, reopened.CreatedAt);
        Assert.Null(reopened.RecoveryDetectedAt);
        Assert.Null(reopened.ResolvedAt);
        Assert.False(reopened.AutoResolved);
        Assert.Equal(expectedStage, reopened.WorkflowStage);
        Assert.Equal(AlertSeverity.Critical, reopened.Severity);
        Assert.Equal("New evidence", reopened.Description);
        Assert.Single(await f.Db.Alerts.ToListAsync());
        Assert.Single(await f.Db.AlertAuditEvents.Where(item => item.EventType == "recovery_lost").ToListAsync());
        Assert.True((await new CameraRepository(f.Db).GetCheckQueueAsync()).Single(item => item.CameraId == 1).HasPhysicalFault);
    }

    [Fact]
    public async Task ConnectivityRecovery_DoesNotClearLensOrClockFaults()
    {
        await using var f = await Fixture.CreateAsync();
        foreach (var type in new[] { AlertType.CameraOffline, AlertType.AuthenticationFailed, AlertType.AutoRecoveryFailed,
                     AlertType.LensObstructed, AlertType.ConfigurationChanged })
            await f.Service.CreateAlertAsync(1, type, AlertSeverity.Warning, type.ToString());

        await f.Repository.MarkConnectivityRecoveredAsync(1);

        Assert.Single(await f.Db.Alerts.ToListAsync());
        Assert.Equal(3, await f.Db.Set<AlertIssue>().CountAsync(issue => issue.RecoveredAt != null));
        Assert.Equal(0, await f.Db.Alerts.CountAsync(alert => alert.AutoResolved));
        Assert.All(await f.Db.Set<AlertIssue>().Where(issue => issue.AlertType == AlertType.LensObstructed ||
            issue.AlertType == AlertType.ConfigurationChanged).ToListAsync(), issue => Assert.Null(issue.RecoveredAt));
    }

    [Fact]
    public async Task RecoveredPhysicalFault_ManagerClosesWithReportWithoutFakingServiceNotification()
    {
        await using var f = await Fixture.CreateAsync();
        var alert = await f.Service.CreateAlertAsync(1, AlertType.LensObstructed, AlertSeverity.Warning, "Obstructed");
        await f.Repository.MarkRecoveredAsync(1, AlertType.LensObstructed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ResolveAlertAsync(alert.Id, 10));
        await f.Service.AssignAlertAsync(alert.Id, 10, 10);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ResolveAlertAsync(alert.Id, 10));
        await f.Service.UpdateAlertWorkAsync(alert.Id, ErrorCategory.Physical, "Recovery verified", false, 10);
        var lastCheck = (await f.Db.Cameras.FindAsync(1))!.LastCheckAt;

        await f.Service.ResolveAlertAsync(alert.Id, 10);

        Assert.True(alert.AutoResolved);
        Assert.NotNull(alert.ResolvedAt);
        Assert.NotNull(alert.RecoveryDetectedAt);
        Assert.Equal("operator", alert.ResolvedByUsername);
        Assert.False(alert.ServiceNotified);
        Assert.Equal(lastCheck, (await f.Db.Cameras.FindAsync(1))!.LastCheckAt);
    }

    private sealed class Fixture(SqliteConnection connection, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public AlertRepository Repository { get; } = new(db);
        public AlertService Service { get; } = new(new AlertRepository(db), NullLogger<AlertService>.Instance);

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            db.Cameras.AddRange(Enumerable.Range(1, 2).Select(id => new Camera
            {
                Id = id,
                Name = "Camera " + id,
                LastCheckAt = DateTime.UtcNow.AddMinutes(-5),
                CheckIntervalSeconds = 3600
            }));
            db.AppUsers.Add(new AppUser { Id = 10, Username = "operator", DisplayName = "Operator", Role = "Operator", CanRunChecks = true });
            await db.SaveChangesAsync();
            return new(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
