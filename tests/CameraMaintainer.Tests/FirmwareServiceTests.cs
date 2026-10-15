using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CameraMaintainer.Tests;

public sealed class FirmwareServiceTests
{
    [Fact]
    public async Task RecordVersionAsync_SameVersionWithUnknownHardware_DoesNotCreateChange()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(CreateCamera());
        await db.SaveChangesAsync();
        var service = new FirmwareService(db);

        var initial = await service.RecordVersionAsync(
            1,
            "2.800.0000000.7.R, Build Date 2020-06-16",
            "IPC-HW-42A",
            "Dahua",
            "IPC-K42A");
        var duplicate = await service.RecordVersionAsync(
            1,
            "  2.800.0000000.7.R,   Build Date 2020-06-16  ",
            "Unknown",
            "Dahua",
            "IPC-K42A");

        Assert.NotNull(initial);
        Assert.Null(duplicate);
        Assert.Equal(1, await db.FirmwareRecords.CountAsync());
    }

    [Fact]
    public async Task RecordVersionAsync_DifferentVersion_CreatesChange()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Cameras.Add(CreateCamera());
        await db.SaveChangesAsync();
        var service = new FirmwareService(db);

        await service.RecordVersionAsync(
            1,
            "2.800.0000000.7.R",
            "IPC-HW-42A",
            "Dahua",
            "IPC-K42A");
        var changed = await service.RecordVersionAsync(
            1,
            "2.801.0000000.7.R",
            "Unknown",
            "Dahua",
            "IPC-K42A");

        Assert.NotNull(changed);
        Assert.Equal(2, await db.FirmwareRecords.CountAsync());
    }

    [Fact]
    public async Task IsUpToDate_UsesConfiguredInstanceBaseline()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        var service = new FirmwareService(
            db,
            baselineVersions: new Dictionary<string, string>
            {
                ["Dahua|IPC-K42A"] = "2.801.0"
            });

        Assert.False(service.IsUpToDate("Dahua", "IPC-K42A", "2.800.0"));
        Assert.True(service.IsUpToDate("Dahua", "IPC-K42A", "2.801.0"));
        Assert.True(service.IsUpToDate("Unknown", "Other", "1.0"));
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
            Name = "1",
            ServiceUrl = "http://192.168.4.31/onvif/device_service"
        };
    }
}
