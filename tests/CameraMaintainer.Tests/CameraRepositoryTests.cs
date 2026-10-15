using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CameraMaintainer.Tests;

public sealed class CameraRepositoryTests
{
    [Fact]
    public async Task GetDueCameraIdsAsync_ReturnsAscendingIdsIncludingActivePhysicalFaults()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var old = DateTime.UtcNow.AddHours(-2);
        db.Cameras.AddRange(
            Camera(3, CameraStatus.Online, autoBackup: true),
            Camera(1, CameraStatus.Online, autoBackup: true),
            Camera(2, CameraStatus.Online, autoBackup: true),
            Camera(4, CameraStatus.Online, autoBackup: true));
        foreach (var camera in db.Cameras.Local)
        {
            camera.LastCheckAt = old;
            camera.CheckIntervalSeconds = 3600;
        }
        db.Alerts.Add(new Alert
        {
            CameraId = 2,
            Category = ErrorCategory.Physical,
            AlertType = AlertType.LensObstructed,
            Title = "Lens obstructed",
            AutoResolved = false
        });
        db.Alerts.Add(new Alert
        {
            CameraId = 4,
            Category = ErrorCategory.Physical,
            AlertType = AlertType.LensObstructed,
            Title = "Old lens alert",
            AutoResolved = true
        });
        await db.SaveChangesAsync();

        var repository = new CameraRepository(db);
        var result = await repository.GetDueCameraIdsAsync(DateTime.UtcNow);

        Assert.Equal(new[] { 1, 2, 3, 4 }, result);
        var queue = await repository.GetCheckQueueAsync();
        Assert.True(queue.Single(item => item.CameraId == 2).HasPhysicalFault);
        Assert.False(queue.Single(item => item.CameraId == 4).HasPhysicalFault);
    }

    [Fact]
    public async Task PhysicalControlChecks_RespectIntervalDisabledCamerasAndConfiguredOrder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var now = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
        for (var id = 1; id <= 4; id++)
        {
            var camera = Camera(id, CameraStatus.Online, autoBackup: false);
            camera.CheckQueuePosition = 5 - id;
            camera.CheckIntervalSeconds = 3600;
            camera.LastCheckAt = now.AddHours(-1);
            camera.AutomaticChecksEnabled = id != 3;
            if (id == 2) camera.LastCheckAt = now.AddMinutes(-59);
            db.Cameras.Add(camera);
            db.Alerts.Add(new Alert { CameraId = id, Category = ErrorCategory.Physical, AlertType = AlertType.LensObstructed });
        }
        await db.SaveChangesAsync();
        var repository = new CameraRepository(db);

        Assert.Equal(new[] { 4, 1 }, await repository.GetDueCameraIdsAsync(now));
        Assert.Equal(new[] { 4, 2, 1 }, await repository.GetDueCameraIdsAsync(now.AddMinutes(1)));
        Assert.Equal(new[] { 4, 3, 2, 1 }, (await repository.GetCheckQueueAsync()).Select(item => item.CameraId));
    }

    [Fact]
    public async Task GetCameraIdsDueForBackupAsync_ReturnsOnlyEligibleCameras()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        db.Cameras.AddRange(
            Camera(1, CameraStatus.Online, autoBackup: true),
            Camera(2, CameraStatus.Online, autoBackup: true),
            Camera(3, CameraStatus.Offline, autoBackup: true),
            Camera(4, CameraStatus.Online, autoBackup: false),
            Camera(5, CameraStatus.Online, autoBackup: true));
        db.ConfigBackups.AddRange(
            new ConfigBackup { CameraId = 2, CreatedAt = now.AddDays(-1) },
            new ConfigBackup { CameraId = 5, CreatedAt = now.AddDays(-10) });
        await db.SaveChangesAsync();

        var repository = new CameraRepository(db);
        var result = await repository.GetCameraIdsDueForBackupAsync(now.AddDays(-7));

        Assert.Equal(new[] { 1, 5 }, result);
    }

    [Fact]
    public async Task CheckQueue_PersistsOrderIntervalsAndAutomaticCheckState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var old = DateTime.UtcNow.AddHours(-2);
        db.Cameras.AddRange(
            Camera(1, CameraStatus.Online, autoBackup: true),
            Camera(2, CameraStatus.Online, autoBackup: true),
            Camera(3, CameraStatus.Online, autoBackup: true));
        foreach (var camera in db.Cameras.Local)
            camera.LastCheckAt = old;
        await db.SaveChangesAsync();

        var repository = new CameraRepository(db);
        await repository.UpdateCheckQueueAsync(new[]
        {
            new CameraCheckQueueUpdate(1, 30, true, 1800),
            new CameraCheckQueueUpdate(2, 10, false, 900),
            new CameraCheckQueueUpdate(3, 20, true, 1200)
        });

        var queue = await repository.GetCheckQueueAsync();
        var dueIds = await repository.GetDueCameraIdsAsync(DateTime.UtcNow);

        Assert.Equal(new[] { 2, 3, 1 }, queue.Select(item => item.CameraId));
        Assert.Equal(new[] { 1, 2, 3 }, queue.Select(item => item.Position));
        Assert.False(queue[0].AutomaticChecksEnabled);
        Assert.Equal(900, queue[0].CheckIntervalSeconds);
        Assert.Equal(new[] { 3, 1 }, dueIds);
    }

    [Fact]
    public async Task SetAutomaticChecksEnabledAsync_KeepsCameraAndControlsCheckCycle()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options);
        await db.Database.EnsureCreatedAsync();

        var now = new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc);
        var camera = Camera(1, CameraStatus.Online, autoBackup: true);
        camera.LastCheckAt = now.AddHours(-1);
        camera.CheckIntervalSeconds = 60;
        db.Cameras.Add(camera);
        await db.SaveChangesAsync();

        var repository = new CameraRepository(db);

        Assert.True(await repository.SetAutomaticChecksEnabledAsync(camera.Id, false));
        Assert.Empty(await repository.GetDueCameraIdsAsync(now));
        Assert.NotNull(await repository.GetByIdAsync(camera.Id));
        Assert.False((await repository.GetCheckQueueAsync()).Single().AutomaticChecksEnabled);
        Assert.Equal(new[] { camera.Id }, (await repository.GetAllAsync(new CameraFilter { AutomaticChecksEnabled = false })).Select(item => item.Id));
        Assert.Empty(await repository.GetAllAsync(new CameraFilter { AutomaticChecksEnabled = true }));

        Assert.True(await repository.SetAutomaticChecksEnabledAsync(camera.Id, true));
        Assert.Equal(new[] { camera.Id }, await repository.GetDueCameraIdsAsync(now));
        Assert.False(await repository.SetAutomaticChecksEnabledAsync(999, false));
    }

    private static Camera Camera(int id, CameraStatus status, bool autoBackup) => new()
    {
        Id = id,
        Name = $"Camera {id}",
        ServiceUrl = $"http://192.0.2.{id}/onvif/device_service",
        IpAddress = $"192.0.2.{id}",
        SerialNumber = $"serial-{id}",
        Status = status,
        AutoBackup = autoBackup
    };
}
