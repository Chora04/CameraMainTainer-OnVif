using CameraMaintainer.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CameraMaintainer.Tests;

public sealed class AlertSerialNumberMigrationTests
{
    [Fact]
    public async Task Migration_BackfillsIndependentSequencesForEachCamera()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260930051704_CameraLocation");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO cameras (Id, Name, ServiceUrl, Port, Status, ConsecutiveFailures,
                ConsecutiveHealthyChecks, PtzSupported, EventsSupported, Tags, Notes,
                AutoBackup, AutoRecovery, CheckIntervalSeconds, AutomaticChecksEnabled, CreatedAt, UpdatedAt)
            VALUES (1, '34', '', 80, 'Online', 0, 0, 0, 0, '[]', '', 0, 0, 3600, 1, '2026-09-01', '2026-09-01'),
                   (2, '35', '', 80, 'Online', 0, 0, 0, 0, '[]', '', 0, 0, 3600, 1, '2026-09-01', '2026-09-01');
            INSERT INTO alerts (Id, CameraId, Severity, AlertType, Category, Title, AutoResolved,
                ServiceNotified, CreatedAt, UpdatedAt, WorkflowStage)
            VALUES (10, 1, 'Warning', 'CameraOffline', 'Technical', 'First', 1, 0, '2026-09-01', '2026-09-01', 'ready'),
                   (11, 2, 'Warning', 'CameraOffline', 'Technical', 'Other camera', 1, 0, '2026-09-01', '2026-09-01', 'ready'),
                   (12, 1, 'Warning', 'LensObstructed', 'Physical', 'Second', 1, 0, '2026-09-02', '2026-09-02', 'ready');
            """);

        await migrator.MigrateAsync();

        var sequences = await db.Alerts.IgnoreQueryFilters()
            .OrderBy(alert => alert.Id)
            .Select(alert => alert.CameraSequenceNumber)
            .ToListAsync();
        Assert.Equal(new int?[] { 1, 1, 2 }, sequences);
        Assert.Equal(3, (await db.Cameras.SingleAsync(camera => camera.Id == 1)).NextAlertSequence);
        Assert.Equal(2, (await db.Cameras.SingleAsync(camera => camera.Id == 2)).NextAlertSequence);
    }
}
