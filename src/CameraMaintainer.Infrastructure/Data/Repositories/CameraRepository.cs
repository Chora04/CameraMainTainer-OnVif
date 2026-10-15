using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace CameraMaintainer.Infrastructure.Data.Repositories;

public class CameraRepository : ICameraRepository
{
    private const string CompletedCyclesKey = "cameras.monitoring.completed-cycles";
    private readonly AppDbContext _db;
    private readonly IMonitoringEvents? _events;

    public CameraRepository(AppDbContext db, IMonitoringEvents? events = null)
    {
        _db = db;
        _events = events;
    }

    public async Task<Camera?> GetByIdAsync(int id)
    {
        return await _db.Cameras
            .Include(c => c.Credential)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<List<int>> GetExistingIdsAsync(IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
            return [];

        return await _db.Cameras
            .AsNoTracking()
            .Where(camera => ids.Contains(camera.Id))
            .Select(camera => camera.Id)
            .ToListAsync();
    }

    private IQueryable<Camera> FilterCameras(CameraFilter filter)
    {
        var query = _db.Cameras.AsNoTracking();

        if (!string.IsNullOrEmpty(filter.Status))
        {
            if (!Enum.TryParse<CameraStatus>(filter.Status, true, out var status) || !Enum.IsDefined(status))
                return query.Where(c => false);
            query = query.Where(c => c.Status == status);
        }
        if (filter.AutomaticChecksEnabled.HasValue)
            query = query.Where(c => c.AutomaticChecksEnabled == filter.AutomaticChecksEnabled.Value);
        if (filter.Attention)
            query = query.Where(c => c.Status == CameraStatus.Suspect || c.Status == CameraStatus.Unknown);
        if (filter.AfterId.HasValue)
            query = query.Where(c => c.Id > filter.AfterId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{EscapeLikePattern(filter.Search.Trim())}%";
            query = query.Where(c =>
                EF.Functions.Like(c.Name, pattern, "\\") ||
                (c.IpAddress != null && EF.Functions.Like(c.IpAddress, pattern, "\\")) ||
                (c.Manufacturer != null && EF.Functions.Like(c.Manufacturer, pattern, "\\")) ||
                (c.Model != null && EF.Functions.Like(c.Model, pattern, "\\")));
        }

        if (!string.IsNullOrEmpty(filter.Tags))
            query = query.Where(c => c.Tags.Contains(filter.Tags));

        return query;
    }

    public Task<int> GetCountAsync(CameraFilter? filter = null)
        => FilterCameras(filter ?? new CameraFilter()).CountAsync();

    public Task<Dictionary<CameraStatus, int>> GetStatusCountsAsync()
        => _db.Cameras.AsNoTracking().GroupBy(c => c.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count);

    public Task<List<Camera>> GetByIdsAsync(IReadOnlyCollection<int> ids)
        => _db.Cameras.AsNoTracking().Where(c => ids.Contains(c.Id)).OrderBy(c => c.Id).ToListAsync();

    public async Task<List<Camera>> GetAllAsync(CameraFilter? filter = null)
    {
        filter ??= new CameraFilter();
        var query = FilterCameras(filter);

        // Sorting
        query = filter.SortBy?.ToLowerInvariant() switch
        {
            "name" => filter.SortDesc
                ? query.OrderByDescending(c => c.Name).ThenBy(c => c.Id)
                : query.OrderBy(c => c.Name).ThenBy(c => c.Id),
            "status" => filter.SortDesc
                ? query.OrderByDescending(c => c.Status).ThenBy(c => c.Id)
                : query.OrderBy(c => c.Status).ThenBy(c => c.Id),
            "ip_address" => filter.SortDesc
                ? query.OrderByDescending(c => c.IpAddress).ThenBy(c => c.Id)
                : query.OrderBy(c => c.IpAddress).ThenBy(c => c.Id),
            "last_check_at" => filter.SortDesc
                ? query.OrderByDescending(c => c.LastCheckAt).ThenBy(c => c.Id)
                : query.OrderBy(c => c.LastCheckAt).ThenBy(c => c.Id),
            "id" => filter.SortDesc ? query.OrderByDescending(c => c.Id) : query.OrderBy(c => c.Id),
            _ => query.OrderBy(c => c.Id)
        };

        var perPage = Math.Clamp(filter.PerPage, 1, 1000);
        var offset = (int)Math.Min((long)(Math.Max(filter.Page, 1) - 1) * perPage, int.MaxValue);
        return await query
            .Skip(offset)
            .Take(perPage)
            .ToListAsync();
    }

    public async Task<Camera> AddAsync(Camera camera)
    {
        camera.CreatedAt = DateTime.UtcNow;
        camera.UpdatedAt = DateTime.UtcNow;
        _db.Cameras.Add(camera);
        await _db.SaveChangesAsync();
        if (_events != null) await _events.CameraRegistryChangedAsync();
        return camera;
    }

    public async Task<Camera> UpdateAsync(Camera camera)
    {
        camera.UpdatedAt = DateTime.UtcNow;
        _db.Cameras.Update(camera);
        await _db.SaveChangesAsync();
        return camera;
    }

    public async Task DeleteAsync(int id)
    {
        var camera = await _db.Cameras
            .Include(c => c.Credential)
            .Include(c => c.PtzPresets)
            .Include(c => c.ConfigBackups)
            .Include(c => c.CurrentHealth)
            .Include(c => c.Alerts)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (camera != null)
        {
            if (camera.Credential != null)
                _db.Credentials.Remove(camera.Credential);
            _db.Cameras.Remove(camera);
            await _db.SaveChangesAsync();
            if (_events != null) await _events.CameraRegistryChangedAsync();
        }
    }

    public async Task UpdateStatusAsync(int cameraId, CameraStatus status, DateTime? lastSeen = null)
    {
        var camera = await _db.Cameras.FindAsync(cameraId);
        if (camera == null) return;

        camera.Status = status;
        camera.LastCheckAt = DateTime.UtcNow;
        if (lastSeen.HasValue)
            camera.LastSeenAt = lastSeen;

        if (status == CameraStatus.Online)
            camera.ConsecutiveFailures = 0;
        else if (status == CameraStatus.Suspect || status == CameraStatus.Offline)
            camera.ConsecutiveFailures++;

        camera.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<List<int>> GetDueCameraIdsAsync(DateTime now)
        => (await GetCheckCycleAsync(now)).CameraIds.ToList();

    public async Task<CameraCheckCycle> GetCheckCycleAsync(DateTime now)
    {
        var policy = await new CameraMonitoringPolicyRepository(_db).GetAsync();
        var cycleNumber = checked(await GetCompletedCyclesAsync() + 1);
        var stableCycle = cycleNumber % policy.StableCheckEveryCycles == 0;
        var ids = await _db.Cameras
            .AsNoTracking()
            .Select(c => new
            {
                Camera = c,
                Stable = c.ConsecutiveHealthyChecks >= policy.HealthyChecksRequired &&
                    !c.Alerts.Any(alert => !alert.AutoResolved && alert.RecoveryDetectedAt == null)
            })
            .Where(item =>
                item.Camera.AutomaticChecksEnabled && (!item.Stable || (policy.CheckStableCameras && stableCycle)) &&
                (
                item.Camera.LastCheckAt == null ||
                item.Camera.LastCheckAt.Value.AddSeconds(item.Camera.CheckIntervalSeconds) <= now))
            .OrderBy(item => item.Camera.CheckQueuePosition ?? item.Camera.Id)
            .ThenBy(item => item.Camera.Id)
            .Select(item => item.Camera.Id)
            .ToListAsync();
        return new(cycleNumber, ids, ids.Count > 0 || await _db.Cameras.AnyAsync(camera => camera.AutomaticChecksEnabled));
    }

    private async Task<long> GetCompletedCyclesAsync()
    {
        var value = await _db.Settings.AsNoTracking().Where(setting => setting.Key == CompletedCyclesKey)
            .Select(setting => setting.Value).SingleOrDefaultAsync();
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= 0 ? count : 0;
    }

    public async Task CompleteCheckCycleAsync(CameraCheckCycle cycle, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!cycle.HasEnabledCameras) return;
        var completed = await GetCompletedCyclesAsync();
        if (cycle.Number == completed) return;
        if (cycle.Number != checked(completed + 1))
            throw new InvalidOperationException("Camera check cycles must complete in order.");
        var setting = await _db.Settings.SingleOrDefaultAsync(item => item.Key == CompletedCyclesKey, cancellationToken);
        if (setting == null)
        {
            setting = new Setting { Key = CompletedCyclesKey };
            _db.Settings.Add(setting);
        }
        setting.Value = cycle.Number.ToString(CultureInfo.InvariantCulture);
        setting.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<CameraCheckQueueItem>> GetCheckQueueAsync(IReadOnlyCollection<int>? cameraIds = null)
    {
        var policy = await new CameraMonitoringPolicyRepository(_db).GetAsync();
        var cycleNumber = checked(await GetCompletedCyclesAsync() + 1);
        var query = _db.Cameras.AsNoTracking();
        if (cameraIds != null) query = query.Where(c => cameraIds.Contains(c.Id));
#pragma warning disable CA1860 // EF Core translates Any() to EXISTS; Count would produce less efficient SQL.
        var rows = await query
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.IpAddress,
                c.Status,
                Position = c.CheckQueuePosition ?? c.Id,
                c.AutomaticChecksEnabled,
                c.CheckIntervalSeconds,
                c.LastCheckAt,
                c.ConsecutiveHealthyChecks,
                HasActiveFault = c.Alerts.Any(a => !a.AutoResolved && a.RecoveryDetectedAt == null),
                HasPhysicalFault = c.Alerts.Any(a =>
                    !a.AutoResolved && a.RecoveryDetectedAt == null &&
                    (a.Issues.Any(issue => issue.RecoveredAt == null && issue.Category == ErrorCategory.Physical) ||
                     (!a.Issues.Any() && a.Category == ErrorCategory.Physical)))
            })
            .OrderBy(c => c.Position)
            .ThenBy(c => c.Id)
            .ToListAsync();
#pragma warning restore CA1860

        return rows.Select(c =>
        {
            var stable = c.ConsecutiveHealthyChecks >= policy.HealthyChecksRequired && !c.HasActiveFault;
            var paused = stable && !policy.CheckStableCameras;
            return new CameraCheckQueueItem(
            c.Id,
            c.Name,
            c.IpAddress,
            c.Status,
            c.Position,
            c.AutomaticChecksEnabled,
            c.CheckIntervalSeconds,
            c.LastCheckAt,
            c.HasPhysicalFault, c.ConsecutiveHealthyChecks, stable,
            paused ? null : c.CheckIntervalSeconds, paused, cycleNumber, policy.StableCheckEveryCycles,
            paused ? null : stable ? (int)((policy.StableCheckEveryCycles - cycleNumber % policy.StableCheckEveryCycles) % policy.StableCheckEveryCycles) + 1 : 1);
        }).ToList();
    }

    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    public async Task UpdateCheckQueueAsync(IReadOnlyList<CameraCheckQueueUpdate> items)
    {
        var cameraIds = items.Select(item => item.CameraId).Distinct().ToArray();
        var cameras = await _db.Cameras
            .Where(camera => cameraIds.Contains(camera.Id))
            .ToDictionaryAsync(camera => camera.Id);
        var totalCameraCount = await _db.Cameras.CountAsync();

        if (cameraIds.Length != items.Count || cameras.Count != totalCameraCount)
            throw new InvalidOperationException("Список камер изменился. Обновите очередь и повторите сохранение.");

        var orderedItems = items.OrderBy(item => item.Position).ToArray();
        for (var index = 0; index < orderedItems.Length; index++)
        {
            var item = orderedItems[index];
            var camera = cameras[item.CameraId];
            camera.CheckQueuePosition = index + 1;
            camera.AutomaticChecksEnabled = item.AutomaticChecksEnabled;
            camera.CheckIntervalSeconds = item.CheckIntervalSeconds;
            camera.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<bool> SetAutomaticChecksEnabledAsync(int cameraId, bool enabled)
    {
        var camera = await _db.Cameras.FindAsync(cameraId);
        if (camera == null)
            return false;

        if (camera.AutomaticChecksEnabled == enabled)
            return true;

        camera.AutomaticChecksEnabled = enabled;
        camera.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        if (_events != null) await _events.CameraRegistryChangedAsync();
        return true;
    }

    public async Task<List<int>> GetCameraIdsDueForBackupAsync(DateTime createdBefore)
    {
        return await _db.Cameras
            .AsNoTracking()
            .Where(camera =>
                camera.Status == CameraStatus.Online &&
                camera.AutoBackup &&
                !camera.ConfigBackups.Any(backup => backup.CreatedAt >= createdBefore))
            .OrderBy(camera => camera.Id)
            .Select(camera => camera.Id)
            .ToListAsync();
    }

    public async Task<Camera?> GetByIpAsync(string ipAddress)
    {
        return await _db.Cameras
            .Include(c => c.Credential)
            .FirstOrDefaultAsync(c => c.IpAddress == ipAddress);
    }

}
