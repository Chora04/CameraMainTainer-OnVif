using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CameraMaintainer.Tests;

public sealed class MonitoringEventsTests
{
    [Fact]
    public async Task HealthEvent_IsPublishedAfterEachSavedResult_ForThatCameraOnly()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(new Camera { Id = 35, Name = "Camera 35" });
        await db.SaveChangesAsync();
        var events = new TestEvents
        {
            OnHealth = async id => Assert.Equal(id, (await db.HealthRecords.AsNoTracking().SingleAsync()).CameraId)
        };
        var repository = new HealthRepository(db, events);

        await repository.UpsertLatestAsync(new HealthRecord { CameraId = 35, Reachable = true });
        await repository.UpsertLatestAsync(new HealthRecord { CameraId = 35, Reachable = false });

        Assert.Equal(new[] { 35, 35 }, events.CameraIds);
        Assert.False((await db.HealthRecords.AsNoTracking().SingleAsync()).Reachable);
    }

    [Fact]
    public async Task FailedHealthSave_DoesNotPublishAnEvent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var events = new TestEvents();
        var repository = new HealthRepository(db, events);

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.UpsertLatestAsync(new HealthRecord { CameraId = 999 }));
        Assert.Empty(events.CameraIds);
    }

    [Fact]
    public async Task AlertEvents_NotifyOnCreationUpdateRecoveryAndViewAudit_ButNotNoOp()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(new Camera { Id = 1, Name = "Camera 1" });
        await db.SaveChangesAsync();
        var events = new TestEvents
        {
            OnAlerts = async () => Assert.True(await db.Alerts.AsNoTracking().AnyAsync())
        };
        var repository = new AlertRepository(db, events);
        var alert = await repository.AddAsync(new Alert { CameraId = 1, AlertType = AlertType.CameraOffline, Title = "Offline" });
        Assert.Equal(1, events.AlertsChanged);
        alert.WorkReport = "Checking";
        await repository.UpdateAsync(alert);
        Assert.Equal(2, events.AlertsChanged);
        await repository.AddAuditEventAsync(new AlertAuditEvent { AlertId = alert.Id, EventType = "viewed" });
        Assert.Equal(3, events.AlertsChanged);
        await repository.MarkConnectivityRecoveredAsync(1);
        Assert.Equal(4, events.AlertsChanged);
        await repository.MarkConnectivityRecoveredAsync(1);
        await repository.MarkRecoveredAsync(1, AlertType.CameraOffline);
        Assert.Equal(4, events.AlertsChanged);
        await repository.AddAsync(new Alert { CameraId = 1, AlertType = AlertType.LensObstructed, Title = "Obstructed" });
        await repository.MarkRecoveredAsync(1, AlertType.LensObstructed);
        Assert.Equal(6, events.AlertsChanged);
        Assert.All(await db.Alerts.AsNoTracking().ToListAsync(), item =>
        {
            Assert.False(item.AutoResolved);
            Assert.Equal("ready", item.WorkflowStage);
            Assert.NotNull(item.RecoveryDetectedAt);
        });
    }

    [Fact]
    public async Task RegistryEvents_NotifyMembershipChangesButNotEveryRoutineCameraWrite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var events = new TestEvents();
        var repository = new CameraRepository(db, events);
        var camera = await repository.AddAsync(new Camera { Name = "Test" });
        Assert.Equal(1, events.RegistryChanged);
        await repository.UpdateAsync(camera);
        Assert.Equal(1, events.RegistryChanged);
        await repository.DeleteAsync(camera.Id);
        Assert.Equal(2, events.RegistryChanged);
        await repository.DeleteAsync(camera.Id);
        Assert.Equal(2, events.RegistryChanged);
    }

    private static AppDbContext CreateContext(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

    private sealed class TestEvents : IMonitoringEvents
    {
        public int RegistryChanged { get; private set; }
        public Task CameraRegistryChangedAsync() { RegistryChanged++; return Task.CompletedTask; }
        public Task MaintenanceChangedAsync() => Task.CompletedTask;
        public List<int> CameraIds { get; } = [];
        public int AlertsChanged { get; private set; }
        public Func<int, Task>? OnHealth { get; init; }
        public Func<Task>? OnAlerts { get; init; }

        public async Task HealthUpdatedAsync(int cameraId)
        {
            CameraIds.Add(cameraId);
            if (OnHealth != null) await OnHealth(cameraId);
        }

        public async Task AlertsChangedAsync()
        {
            AlertsChanged++;
            if (OnAlerts != null) await OnAlerts();
        }
    }
}
