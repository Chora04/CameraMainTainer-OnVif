using System.Diagnostics;
using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Web.Controllers;
using CameraMaintainer.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CameraMaintainer.Tests;

public sealed class LargeFleetTests(ITestOutputHelper output)
{
    [Fact]
    public async Task TwentyThousandCameras_PagingCountsAndQueueDoNotTruncateOrTrackFleet()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.AddRange(Enumerable.Range(1, 20_000).Select(id => new Camera
        {
            Id = id,
            Name = $"Camera {id}",
            ServiceUrl = $"http://camera-{id}/onvif",
            Status = id % 2 == 0 ? CameraStatus.Online : CameraStatus.Unknown,
            Credential = new Credential { Username = "camera-user", PasswordEnc = "encrypted-test" }
        }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var repo = new CameraRepository(db);
        var timer = Stopwatch.StartNew();
        var count = await repo.GetCountAsync();
        var last = await repo.GetAllAsync(new CameraFilter { Page = 400, PerPage = 50, SortBy = "id" });
        output.WriteLine($"20,000 cameras: count + last page (50 rows): {timer.ElapsedMilliseconds} ms");
        Assert.Equal(20_000, count);
        Assert.Equal(Enumerable.Range(19_951, 50), last.Select(c => c.Id));
        Assert.All(last, camera => Assert.Null(camera.Credential));
        var counts = await repo.GetStatusCountsAsync();
        Assert.Equal(10_000, counts[CameraStatus.Online]);
        Assert.Equal(10_000, await repo.GetCountAsync(new CameraFilter { Attention = true }));
        var filtered = await repo.GetAllAsync(new CameraFilter { Attention = true, Page = 200, PerPage = 50 });
        Assert.Equal(50, filtered.Count);
        Assert.Equal(19_999, filtered[^1].Id);
        var batch = await repo.GetAllAsync(new CameraFilter { AfterId = 19_998, PerPage = 100 });
        Assert.Equal(new[] { 19_999, 20_000 }, batch.Select(c => c.Id));
        Assert.Equal(new[] { 1, 20_000 }, (await repo.GetByIdsAsync(new[] { 20_000, 1 })).Select(c => c.Id));
        timer.Restart();
        var cycle = await repo.GetCheckCycleAsync(DateTime.UtcNow);
        output.WriteLine($"20,000 cameras: ordered cycle ID selection: {timer.ElapsedMilliseconds} ms");
        Assert.Equal(Enumerable.Range(1, 20_000), cycle.CameraIds);
        var partialQueue = await repo.GetCheckQueueAsync(new[] { 1, 20_000 });
        Assert.Equal(new[] { 1, 20_000 }, partialQueue.Select(item => item.CameraId));
        Assert.Equal(20_000, partialQueue[^1].Position);
        Assert.Empty(db.ChangeTracker.Entries());
        using var snapshots = new SnapshotCache();
        var controller = new CamerasController(repo, null!, null!, null!, null!, null!, null!, NullLogger<CamerasController>.Instance, snapshots);
        var lookup = Assert.IsType<OkObjectResult>((await controller.GetVisible(new[] { 20_000, 1, 1 })).Result);
        var lookupCameras = Assert.IsType<List<Camera>>(lookup.Value);
        Assert.Equal(new[] { 1, 20_000 }, lookupCameras.Select(camera => camera.Id));
        Assert.All(lookupCameras, camera => Assert.Null(camera.Credential));
        Assert.IsType<BadRequestObjectResult>((await controller.GetVisible(Enumerable.Range(1, 201).ToArray())).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.GetVisible(new[] { -1 })).Result);
        Assert.Empty(await repo.GetAllAsync(new CameraFilter { Status = "invalid" }));
        Assert.Empty(await repo.GetAllAsync(new CameraFilter { Page = int.MaxValue, PerPage = int.MaxValue }));
    }
}
