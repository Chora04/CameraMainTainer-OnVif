using CameraMaintainer.Core.Models;
using CameraMaintainer.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CameraMaintainer.Infrastructure.Data.Repositories;

public class HealthRepository
{
    private readonly AppDbContext _db;
    private readonly IMonitoringEvents? _events;

    public HealthRepository(AppDbContext db, IMonitoringEvents? events = null)
    {
        _db = db;
        _events = events;
    }

    public async Task<HealthRecord> UpsertLatestAsync(HealthRecord record, bool passedAllChecks = false)
    {
        var gate = CameraAlertLocks.ForCamera(record.CameraId);
        await gate.WaitAsync();
        try { return await UpsertLatestCoreAsync(record, passedAllChecks); }
        finally { gate.Release(); }
    }

    private async Task<HealthRecord> UpsertLatestCoreAsync(HealthRecord record, bool passedAllChecks)
    {
        var current = await _db.HealthRecords
            .SingleOrDefaultAsync(h => h.CameraId == record.CameraId);
        var isNewResult = current == null || record.CheckedAt > current.CheckedAt;

        if (current == null)
        {
            _db.HealthRecords.Add(record);
            current = record;
        }
        else
        {
            record.Id = current.Id;
            _db.Entry(current).CurrentValues.SetValues(record);
        }

        if (isNewResult)
        {
            var camera = await _db.Cameras.FindAsync(record.CameraId);
            if (camera != null)
            {
                var healthy = passedAllChecks && record.Reachable && record.StatusCode == "ok" &&
                    !await _db.Alerts.AnyAsync(alert => alert.CameraId == record.CameraId && !alert.AutoResolved && alert.RecoveryDetectedAt == null);
                camera.ConsecutiveHealthyChecks = healthy ? Math.Min(camera.ConsecutiveHealthyChecks + 1, 1000000) : 0;
            }
        }
        await _db.SaveChangesAsync();
        if (_events != null)
            await _events.HealthUpdatedAsync(record.CameraId);
        return current;
    }

    public async Task<HealthRecord?> GetLatestAsync(int cameraId)
    {
        return await _db.HealthRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(h => h.CameraId == cameraId);
    }

    public async Task<double?> GetAverageTemperatureAsync()
    {
        var average = await _db.HealthRecords
            .Where(h => h.TemperatureC != null && h.CheckedAt > DateTime.UtcNow.AddHours(-1))
            .Select(h => h.TemperatureC)
            .AverageAsync();

        return average;
    }
}
