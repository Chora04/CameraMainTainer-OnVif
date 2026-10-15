using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Services;

/// <summary>
/// Tracks firmware versions across cameras and detects upgrades/downgrades.
/// Maintains a configurable baseline of minimum expected firmware versions per model.
/// </summary>
public class FirmwareService : IFirmwareService
{
    private readonly AppDbContext _db;
    private readonly ILogger<FirmwareService>? _logger;

    // In-memory baseline: "Manufacturer|Model" -> minimum acceptable version string.
    private readonly Dictionary<string, string> _baselineVersions;

    public FirmwareService(
        AppDbContext db,
        ILogger<FirmwareService>? logger = null,
        IReadOnlyDictionary<string, string>? baselineVersions = null)
    {
        _db = db;
        _logger = logger;
        _baselineVersions = (baselineVersions ?? new Dictionary<string, string>())
            .ToDictionary(pair => pair.Key, pair => NormalizeVersion(pair.Value), StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<FirmwareRecord?> RecordVersionAsync(
        int cameraId, string firmwareVersion, string hardwareId,
        string manufacturer, string model)
    {
        firmwareVersion = NormalizeVersion(firmwareVersion);

        // Check if version actually changed
        var lastRecord = await _db.Set<FirmwareRecord>()
            .AsNoTracking()
            .Where(r => r.CameraId == cameraId)
            .OrderByDescending(r => r.DetectedAt)
            .FirstOrDefaultAsync();

        if (lastRecord != null &&
            FirmwareVersionsEqual(lastRecord.FirmwareVersion, firmwareVersion))
        {
            return null; // No change — skip recording
        }

        var isCurrent = IsUpToDate(manufacturer, model, firmwareVersion);

        var record = new FirmwareRecord
        {
            CameraId = cameraId,
            FirmwareVersion = firmwareVersion,
            HardwareId = hardwareId,
            Manufacturer = manufacturer,
            Model = model,
            IsCurrent = isCurrent,
            DetectedAt = DateTime.UtcNow
        };

        _db.Set<FirmwareRecord>().Add(record);
        await _db.SaveChangesAsync();

        if (lastRecord != null)
        {
            var verb = IsVersionNewer(firmwareVersion, lastRecord.FirmwareVersion) ? "UPGRADE" : "DOWNGRADE";
            _logger?.LogWarning("Firmware {Change} detected for camera {CameraId}: {OldVer} → {NewVer}",
                verb, cameraId, lastRecord.FirmwareVersion, firmwareVersion);
        }
        else
        {
            _logger?.LogInformation("Initial firmware record for camera {CameraId}: {Version}",
                cameraId, firmwareVersion);
        }

        return record;
    }

    /// <inheritdoc />
    public bool IsUpToDate(string manufacturer, string model, string firmwareVersion)
    {
        var key = $"{manufacturer}|{model}";
        if (_baselineVersions.TryGetValue(key, out var minimumVersion))
        {
            return !IsVersionNewer(minimumVersion, firmwareVersion);
        }

        // No baseline configured — assume current
        return true;
    }

    /// <inheritdoc />
    public async Task<List<FirmwareRecord>> GetHistoryAsync(int cameraId, int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 200);
        return await _db.Set<FirmwareRecord>()
            .AsNoTracking()
            .Where(r => r.CameraId == cameraId)
            .OrderByDescending(r => r.DetectedAt)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Simple semantic version comparison.
    /// Returns true if versionA is newer (greater) than versionB.
    /// Handles formats: "1.2.3", "2.4.0", "v1.0", "V5.3.1.20190912"
    /// </summary>
    private static bool IsVersionNewer(string versionA, string versionB)
    {
        var partsA = ParseVersionParts(versionA);
        var partsB = ParseVersionParts(versionB);

        int maxLen = Math.Max(partsA.Length, partsB.Length);
        for (int i = 0; i < maxLen; i++)
        {
            int a = i < partsA.Length ? partsA[i] : 0;
            int b = i < partsB.Length ? partsB[i] : 0;
            if (a > b) return true;
            if (a < b) return false;
        }

        return false;
    }

    private static int[] ParseVersionParts(string version)
    {
        // Strip leading 'v' or 'V'
        var cleaned = version.TrimStart('v', 'V');
        return cleaned.Split('.', '-', '_')
            .Select(part => int.TryParse(part, out var n) ? n : 0)
            .ToArray();
    }

    private static bool FirmwareVersionsEqual(string? left, string? right)
    {
        return string.Equals(
            NormalizeVersion(left),
            NormalizeVersion(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return string.Empty;

        return string.Join(' ', version.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
