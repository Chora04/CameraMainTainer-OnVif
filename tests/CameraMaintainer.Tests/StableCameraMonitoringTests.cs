using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using CameraMaintainer.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using CameraMaintainer.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CameraMaintainer.Tests;

public sealed class StableCameraMonitoringTests
{
    [Fact]
    public async Task Policy_DefaultsPersistsAndRejectsInvalidValues()
    {
        await using var f = await Fixture.CreateAsync();
        var repository = new CameraMonitoringPolicyRepository(f.Db);
        Assert.Equal(new CameraMonitoringPolicy(), await repository.GetAsync());
        var policy = new CameraMonitoringPolicy { HealthyChecksRequired = 8, CheckStableCameras = false, StableCheckEveryCycles = 3 };
        await repository.SaveAsync(policy);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(policy, await new CameraMonitoringPolicyRepository(f.Db).GetAsync());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.SaveAsync(policy with { HealthyChecksRequired = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.SaveAsync(policy with { StableCheckEveryCycles = 25 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.SaveAsync(policy with { FrameHistoryLimitPerCamera = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.SaveAsync(policy with { NormalBrightnessMinimum = 230, NormalBrightnessMaximum = 220 }));
        Assert.Equal(policy, await repository.GetAsync());
    }

    [Fact]
    public async Task Stability_RequiresFiveCompleteSuccessesAndResetsOnFailure()
    {
        await using var f = await Fixture.CreateAsync();
        var health = new HealthRepository(f.Db);
        var cameras = new CameraRepository(f.Db);
        var start = DateTime.UtcNow.AddHours(-2);
        for (var i = 1; i <= 5; i++)
        {
            await health.UpsertLatestAsync(Result(start.AddMinutes(i)), passedAllChecks: true);
            Assert.Equal(i, f.Camera.ConsecutiveHealthyChecks);
            Assert.Equal(i == 5, (await cameras.GetCheckQueueAsync()).Single().IsStable);
        }
        await health.UpsertLatestAsync(Result(start.AddMinutes(5)), passedAllChecks: true);
        Assert.Equal(5, f.Camera.ConsecutiveHealthyChecks);
        Assert.Equal(3600, (await cameras.GetCheckQueueAsync()).Single().EffectiveCheckIntervalSeconds);
        Assert.Equal(2, (await cameras.GetCheckQueueAsync()).Single().CyclesUntilCheck);
        Assert.Equal(1, (await cameras.GetCheckCycleAsync(start)).Number);

        await health.UpsertLatestAsync(Result(start.AddMinutes(6)), passedAllChecks: false);
        Assert.Equal(0, f.Camera.ConsecutiveHealthyChecks);
        await health.UpsertLatestAsync(Result(start.AddMinutes(7)), passedAllChecks: true);
        Assert.Equal(1, f.Camera.ConsecutiveHealthyChecks);
        var failed = Result(start.AddMinutes(8));
        failed.Reachable = false;
        failed.StatusCode = "timeout";
        await health.UpsertLatestAsync(failed, passedAllChecks: true);
        Assert.Equal(0, f.Camera.ConsecutiveHealthyChecks);
        Assert.Equal(3600, (await cameras.GetCheckQueueAsync()).Single().EffectiveCheckIntervalSeconds);
    }

    [Fact]
    public async Task AnyActiveIssue_ResetsStabilityUntilAllIssuesRecover()
    {
        await using var f = await Fixture.CreateAsync();
        f.Camera.ConsecutiveHealthyChecks = 5;
        await f.Db.SaveChangesAsync();
        var service = new AlertService(new AlertRepository(f.Db), NullLogger<AlertService>.Instance);
        var card = await service.CreateAlertAsync(1, AlertType.LensObstructed, AlertSeverity.Warning, "Lens");
        await service.CreateAlertAsync(1, AlertType.ConfigurationChanged, AlertSeverity.Warning, "Clock");
        Assert.Equal(0, f.Camera.ConsecutiveHealthyChecks);
        var health = new HealthRepository(f.Db);
        var now = DateTime.UtcNow;
        await new AlertRepository(f.Db).MarkRecoveredAsync(1, AlertType.LensObstructed);
        await health.UpsertLatestAsync(Result(now), passedAllChecks: true);
        Assert.Equal(0, f.Camera.ConsecutiveHealthyChecks);
        Assert.Null(card.RecoveryDetectedAt);
        await new AlertRepository(f.Db).MarkRecoveredAsync(1, AlertType.ConfigurationChanged);
        await health.UpsertLatestAsync(Result(now.AddMinutes(1)), passedAllChecks: true);
        Assert.Equal(1, f.Camera.ConsecutiveHealthyChecks);
        Assert.Equal("ready", card.WorkflowStage);
        Assert.False(card.AutoResolved);
    }

    [Fact]
    public async Task StableSchedule_CountsCompletedPassesNotElapsedTime_AndPreservesOrder()
    {
        await using var f = await Fixture.CreateAsync();
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        f.Camera.LastCheckAt = now.AddHours(-1);
        f.Camera.ConsecutiveHealthyChecks = 5;
        f.Camera.CheckQueuePosition = 3;
        f.Db.Cameras.AddRange(
            new Camera { Id = 2, Name = "Two", LastCheckAt = now.AddHours(-1), CheckIntervalSeconds = 3600, CheckQueuePosition = 1 },
            new Camera { Id = 3, Name = "Three", LastCheckAt = now.AddHours(-1), CheckIntervalSeconds = 3600, CheckQueuePosition = 2 },
            new Camera { Id = 4, Name = "Disabled", AutomaticChecksEnabled = false });
        await f.Db.SaveChangesAsync();
        var cameras = new CameraRepository(f.Db);
        Assert.Equal(new[] { 2, 3 }, await cameras.GetDueCameraIdsAsync(now));
        Assert.Equal(new[] { 2, 3 }, await cameras.GetDueCameraIdsAsync(now.AddDays(1)));
        var first = await cameras.GetCheckCycleAsync(now);
        Assert.Equal(1, first.Number);
        await cameras.CompleteCheckCycleAsync(first);
        Assert.Equal(new[] { 2, 3, 1 }, await cameras.GetDueCameraIdsAsync(now));
        await cameras.CompleteCheckCycleAsync(first);
        Assert.Equal(2, (await cameras.GetCheckCycleAsync(now)).Number);
        await cameras.CompleteCheckCycleAsync(await cameras.GetCheckCycleAsync(now));
        Assert.Equal(new[] { 2, 3 }, await cameras.GetDueCameraIdsAsync(now));

        // A fresh DbContext sees the same cycle after an application restart.
        await using var restartedDb = f.NewContext();
        Assert.Equal(3, (await new CameraRepository(restartedDb).GetCheckCycleAsync(now)).Number);

        var settings = new CameraMonitoringPolicyRepository(f.Db);
        await settings.SaveAsync(new() { StableCheckEveryCycles = 3 });
        Assert.Equal(new[] { 2, 3, 1 }, await cameras.GetDueCameraIdsAsync(now));
        await cameras.CompleteCheckCycleAsync(await cameras.GetCheckCycleAsync(now));
        Assert.Equal(new[] { 2, 3 }, await cameras.GetDueCameraIdsAsync(now.AddDays(1)));
        Assert.Equal(3, (await cameras.GetCheckQueueAsync()).Single(item => item.CameraId == 1).CyclesUntilCheck);
        await settings.SaveAsync(new() { StableCheckEveryCycles = 1 });
        Assert.Equal(new[] { 2, 3, 1 }, await cameras.GetDueCameraIdsAsync(now));
        await settings.SaveAsync(new() { HealthyChecksRequired = 6 });
        Assert.Equal(new[] { 2, 3, 1 }, await cameras.GetDueCameraIdsAsync(now));

        await settings.SaveAsync(new() { CheckStableCameras = false });
        Assert.Equal(new[] { 2, 3 }, await cameras.GetDueCameraIdsAsync(now.AddDays(10)));
        var stable = (await cameras.GetCheckQueueAsync()).Single(item => item.CameraId == 1);
        Assert.True(stable.StableChecksPaused);
        Assert.Null(stable.EffectiveCheckIntervalSeconds);
        var service = new AlertService(new AlertRepository(f.Db), NullLogger<AlertService>.Instance);
        await service.CreateAlertAsync(1, AlertType.CameraOffline, AlertSeverity.Critical, "Offline");
        Assert.Equal(new[] { 2, 3, 1 }, await cameras.GetDueCameraIdsAsync(now));
    }

    [Fact]
    public async Task Cycles_DoNotAdvanceForCancelledPassesOrDisabledQueue()
    {
        await using var f = await Fixture.CreateAsync();
        var cameras = new CameraRepository(f.Db);
        var cycle = await cameras.GetCheckCycleAsync(DateTime.UtcNow);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cameras.CompleteCheckCycleAsync(cycle, new CancellationToken(true)));
        Assert.Equal(1, (await cameras.GetCheckCycleAsync(DateTime.UtcNow)).Number);
        f.Camera.AutomaticChecksEnabled = false;
        await f.Db.SaveChangesAsync();
        await cameras.CompleteCheckCycleAsync(await cameras.GetCheckCycleAsync(DateTime.UtcNow));
        Assert.Equal(1, (await cameras.GetCheckCycleAsync(DateTime.UtcNow)).Number);
    }

    [Fact]
    public async Task StableOnlyQueue_AdvancesSkippedPassAndRetainsMinimumInterval()
    {
        await using var f = await Fixture.CreateAsync();
        f.Camera.ConsecutiveHealthyChecks = 5;
        f.Camera.LastCheckAt = DateTime.UtcNow;
        await f.Db.SaveChangesAsync();
        var cameras = new CameraRepository(f.Db);
        var first = await cameras.GetCheckCycleAsync(DateTime.UtcNow);
        Assert.Empty(first.CameraIds);
        Assert.True(first.HasEnabledCameras);
        await cameras.CompleteCheckCycleAsync(first);
        Assert.Empty(await cameras.GetDueCameraIdsAsync(DateTime.UtcNow.AddMinutes(59)));
        Assert.Equal(new[] { 1 }, await cameras.GetDueCameraIdsAsync(DateTime.UtcNow.AddMinutes(61)));
    }

    [Fact]
    public async Task QueueApi_ShowsSkippedCycleRatherThanInventingNextCheckTime()
    {
        await using var f = await Fixture.CreateAsync();
        f.Camera.ConsecutiveHealthyChecks = 5;
        await f.Db.SaveChangesAsync();
        var cameras = new CameraRepository(f.Db);
        using var gate = new HealthCheckConcurrencyGate(2);
        var controller = new HealthController(null!, cameras, null!, gate);
        JsonElement Data(OkObjectResult result) => JsonSerializer.SerializeToElement(result.Value).GetProperty("items")[0];
        var first = Data(Assert.IsType<OkObjectResult>(await controller.GetCheckQueue()));
        Assert.False(first.GetProperty("DueNow").GetBoolean());
        Assert.Equal("stable-skipped", first.GetProperty("QueueState").GetString());
        Assert.Equal(2, first.GetProperty("CyclesUntilCheck").GetInt32());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("NextCheckAt").ValueKind);
        await cameras.CompleteCheckCycleAsync(await cameras.GetCheckCycleAsync(DateTime.UtcNow));
        var second = Data(Assert.IsType<OkObjectResult>(await controller.GetCheckQueue()));
        Assert.True(second.GetProperty("DueNow").GetBoolean());
        Assert.Equal(2, second.GetProperty("NextCycleNumber").GetInt64());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullPass_CommitsOnlyAfterAllCamerasAndNeverAfterCancellation(bool cancel)
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.Cameras.Add(new Camera { Id = 2, Name = "Two" });
        await f.Db.SaveChangesAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var visited = new List<int>();
        var services = new ServiceCollection();
        services.AddScoped<IHealthService>(_ => new QueueCheckStub(async id =>
        {
            visited.Add(id);
            if (id == 1) { started.SetResult(); await release.Task; }
            return new HealthRecord { CameraId = id };
        }));
        await using var provider = services.BuildServiceProvider();
        using var gate = new HealthCheckConcurrencyGate(1);
        using var cancellation = new CancellationTokenSource();
        var cameras = new CameraRepository(f.Db);
        var service = CreateHealthService(f.Db, gate, provider.GetRequiredService<IServiceScopeFactory>());
        var pass = service.CheckAllDueAsync(cancellation.Token);
        await started.Task;
        Assert.Equal(1, (await cameras.GetCheckCycleAsync(DateTime.UtcNow)).Number);
        if (cancel) cancellation.Cancel();
        release.SetResult();
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pass);
        else await pass;
        Assert.Equal(cancel ? new[] { 1 } : new[] { 1, 2 }, visited);
        Assert.Equal(cancel ? 1 : 2, (await cameras.GetCheckCycleAsync(DateTime.UtcNow)).Number);
    }

    [Fact]
    public async Task ManualSingleCameraCheck_DoesNotAdvanceQueuePass()
    {
        await using var f = await Fixture.CreateAsync();
        using var gate = new HealthCheckConcurrencyGate(1);
        var service = CreateHealthService(f.Db, gate);
        // No credentials: exercises a real check result without network access.
        Assert.Equal("no_credentials", (await service.CheckCameraAsync(1)).StatusCode);
        Assert.Equal(1, (await new CameraRepository(f.Db).GetCheckCycleAsync(DateTime.UtcNow)).Number);
    }

    private static HealthService CreateHealthService(AppDbContext db, HealthCheckConcurrencyGate gate, IServiceScopeFactory? scopes = null) => new(
        cameraRepo: new CameraRepository(db), healthRepo: new HealthRepository(db), alertRepo: new AlertRepository(db),
        onvif: null!, autoRecovery: null!, alertService: new AlertService(new AlertRepository(db), NullLogger<AlertService>.Instance),
        encryptor: null!, breaker: null!, lensDetector: null!, frameProcessor: null!, directionAnalyzer: null!,
        firmwareService: null!, ptzCorrection: null!, concurrencyGate: gate, logger: NullLogger<HealthService>.Instance, scopeFactory: scopes);

    private sealed class QueueCheckStub(Func<int, Task<HealthRecord>> check) : IHealthService
    {
        public Task<HealthRecord> CheckCameraAsync(int cameraId, CancellationToken cancellationToken = default)
            => check(cameraId);
        public Task CheckAllDueAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HealthRecord?> GetCurrentHealthAsync(int cameraId) => throw new NotSupportedException();
        public Task<HealthSummary> GetHealthSummaryAsync() => throw new NotSupportedException();
    }

    private static HealthRecord Result(DateTime time) => new() { CameraId = 1, CheckedAt = time, Reachable = true, StatusCode = "ok" };

    private sealed class Fixture(SqliteConnection connection, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public Camera Camera => Db.Cameras.Local.Single(item => item.Id == 1);
        public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            db.Cameras.Add(new Camera { Id = 1, Name = "One", CheckIntervalSeconds = 3600 });
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
