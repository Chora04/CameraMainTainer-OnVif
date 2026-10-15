using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CameraMaintainer.Tests;

public sealed class CameraFrameArchiveServiceTests
{
    [Fact]
    public async Task NormalFrame_StoresMetricsWithoutImage()
    {
        await using var fixture = await Fixture.CreateAsync();

        var record = await fixture.Service.RecordAsync(Capture(
            brightness: 100,
            contrast: 35,
            entropy: 6.4,
            direction: "Normal"));

        Assert.NotNull(record);
        Assert.False(record.IsAnomalous);
        Assert.Null(record.ImageRelativePath);
        Assert.Empty(Directory.EnumerateFiles(fixture.RootPath, "*", SearchOption.AllDirectories));
        Assert.Equal(100, record.MeanBrightness);
    }

    [Fact]
    public async Task AnomalousFrame_StoresImageOutsideDatabase()
    {
        await using var fixture = await Fixture.CreateAsync();

        var record = await fixture.Service.RecordAsync(Capture(
            brightness: 12,
            contrast: 8,
            entropy: 2.2,
            direction: "DarkScene"));

        Assert.NotNull(record);
        Assert.True(record.IsAnomalous);
        Assert.NotNull(record.ImageRelativePath);
        Assert.Contains("brightness_low", record.TriggerCodes);
        Assert.Null((await fixture.Db.CameraFrameRecords.AsNoTracking().SingleAsync()).Camera);

        var content = await fixture.Service.OpenContentAsync(1, record.Id);
        Assert.NotNull(content);
        await using (content.Stream)
        {
            using var copy = new MemoryStream();
            await content.Stream.CopyToAsync(copy);
            Assert.Equal(TestImage, copy.ToArray());
        }
    }

    [Fact]
    public async Task HistoryLimit_RemovesOldestRecordAndImage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Policy.SaveAsync(new CameraMonitoringPolicy
        {
            FrameHistoryLimitPerCamera = 2
        });

        var first = await fixture.Service.RecordAsync(Capture(10, 5, 1, "DarkScene") with
        {
            CapturedAt = DateTime.UtcNow.AddMinutes(-2)
        });
        await fixture.Service.RecordAsync(Capture(250, 5, 1, "Overexposed") with
        {
            CapturedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        await fixture.Service.RecordAsync(Capture(9, 5, 1, "DarkScene"));

        Assert.Equal(2, await fixture.Db.CameraFrameRecords.CountAsync());
        Assert.Equal(2, Directory.EnumerateFiles(fixture.RootPath, "*", SearchOption.AllDirectories).Count());
        Assert.Null(await fixture.Service.OpenContentAsync(1, first!.Id));
    }

    [Fact]
    public async Task ConfiguredBrightnessRange_StoresOnlyOutOfRangeImage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Policy.SaveAsync(new CameraMonitoringPolicy
        {
            NormalBrightnessMinimum = 45,
            NormalBrightnessMaximum = 75
        });

        var normal = await fixture.Service.RecordAsync(Capture(60, 35, 6.4, "Normal"));
        var tooDark = await fixture.Service.RecordAsync(Capture(44, 35, 6.4, "Normal"));

        Assert.NotNull(normal);
        Assert.False(normal.IsAnomalous);
        Assert.Null(normal.ImageRelativePath);
        Assert.NotNull(tooDark);
        Assert.True(tooDark.IsAnomalous);
        Assert.Contains("brightness_low", tooDark.TriggerCodes);
        Assert.NotNull(tooDark.ImageRelativePath);
        Assert.Single(Directory.EnumerateFiles(fixture.RootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DisabledHistory_DoesNotStoreMetadataOrImage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Policy.SaveAsync(new CameraMonitoringPolicy { StoreFrameHistory = false });

        var record = await fixture.Service.RecordAsync(Capture(10, 5, 1, "DarkScene"));

        Assert.Null(record);
        Assert.Empty(await fixture.Db.CameraFrameRecords.ToListAsync());
        Assert.Empty(Directory.EnumerateFiles(fixture.RootPath, "*", SearchOption.AllDirectories));
    }

    private static readonly byte[] TestImage = [0xFF, 0xD8, 0xFF, 0xDB, 1, 2, 3, 4, 0xFF, 0xD9];

    private static CameraFrameCapture Capture(
        double brightness,
        double contrast,
        double entropy,
        string direction) => new()
        {
            CameraId = 1,
            CapturedAt = DateTime.UtcNow,
            IsObstructed = !direction.Equals("Normal", StringComparison.Ordinal),
            ObstructionType = direction.Equals("Normal", StringComparison.Ordinal) ? "none" : "dark",
            DirectionStatus = direction,
            MeanBrightness = brightness,
            BrightnessStdDev = contrast,
            Entropy = entropy,
            EdgeDensity = 10,
            TextureUniformityScore = 40,
            DirectionConfidence = 0.9,
            EncodedImage = TestImage,
            ImageContentType = "image/jpeg"
        };

    private sealed class Fixture(
        SqliteConnection connection,
        AppDbContext db,
        string rootPath,
        CameraMonitoringPolicyRepository policy,
        CameraFrameArchiveService service) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public string RootPath { get; } = rootPath;
        public CameraMonitoringPolicyRepository Policy { get; } = policy;
        public CameraFrameArchiveService Service { get; } = service;

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();
            db.Cameras.Add(new Camera { Id = 1, Name = "Camera 1", ServiceUrl = "http://camera/onvif" });
            await db.SaveChangesAsync();

            var rootPath = Path.Combine(Path.GetTempPath(), "CameraMaintainer.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var policy = new CameraMonitoringPolicyRepository(db);
            var service = new CameraFrameArchiveService(
                db,
                policy,
                rootPath,
                NullLogger<CameraFrameArchiveService>.Instance);
            return new Fixture(connection, db, rootPath, policy, service);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
    }
}
