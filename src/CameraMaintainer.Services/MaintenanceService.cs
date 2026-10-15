using System.Text.Json;
using System.Xml.Linq;
using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Infrastructure.Security;
using CameraMaintainer.Onvif;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Services;

public class MaintenanceService : IMaintenanceService
{
    private readonly ICameraRepository _cameraRepo;
    private readonly AppDbContext _db;
    private readonly OnvifClient _onvif;
    private readonly ICredentialEncryptor _encryptor;
    private readonly ILogger<MaintenanceService> _logger;
    private readonly IMonitoringEvents? _events;

    public MaintenanceService(
        ICameraRepository cameraRepo,
        AppDbContext db,
        OnvifClient onvif,
        ICredentialEncryptor encryptor,
        ILogger<MaintenanceService> logger,
        IMonitoringEvents? events = null)
    {
        _cameraRepo = cameraRepo;
        _db = db;
        _onvif = onvif;
        _encryptor = encryptor;
        _logger = logger;
        _events = events;
    }

    public async Task<ConfigBackup> BackupConfigAsync(
        int cameraId,
        string backupType = "full",
        string createdBy = "manual",
        CancellationToken cancellationToken = default)
    {
        backupType = NormalizeBackupType(backupType);
        var camera = await GetCameraWithCredential(cameraId);
        var password = _encryptor.Decrypt(camera.Credential!.PasswordEnc);

        var collected = await CollectConfigurationAsync(camera, password, backupType, cancellationToken);

        var backup = new ConfigBackup
        {
            CameraId = cameraId,
            BackupType = collected.IsPartial ? $"{backupType}_partial" : backupType,
            ConfigXml = collected.Xml,
            CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "manual" : createdBy.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _db.ConfigBackups.Add(backup);
        await _db.SaveChangesAsync(cancellationToken);
        if (_events != null) await _events.MaintenanceChangedAsync();

        _logger.LogInformation(
            "Config backup created for camera {CameraId}, type={Type}, partial={IsPartial}",
            cameraId,
            backupType,
            collected.IsPartial);
        return backup;
    }

    public async Task<List<ConfigBackup>> GetBackupsAsync(int cameraId)
    {
        return await _db.ConfigBackups
            .AsNoTracking()
            .Where(b => b.CameraId == cameraId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();
    }

    public async Task<MaintenanceJob> RebootCameraAsync(int cameraId)
    {
        var camera = await GetCameraWithCredential(cameraId);
        var password = _encryptor.Decrypt(camera.Credential!.PasswordEnc);

        _logger.LogWarning("Rebooting camera {CameraId} ({Ip})", cameraId, camera.IpAddress);

        await _onvif.RebootAsync(camera.ServiceUrl, camera.Credential.Username, password, 15000);

        var job = new MaintenanceJob
        {
            JobType = "reboot",
            Status = JobStatus.Completed,
            CameraIds = JsonSerializer.Serialize(new[] { cameraId }),
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        _db.MaintenanceJobs.Add(job);
        await _db.SaveChangesAsync();
        if (_events != null) await _events.MaintenanceChangedAsync();
        return job;
    }

    public async Task<MaintenanceJob> FactoryResetCameraAsync(int cameraId, string mode = "hard")
    {
        var camera = await GetCameraWithCredential(cameraId);
        var password = _encryptor.Decrypt(camera.Credential!.PasswordEnc);

        _logger.LogCritical("Factory resetting camera {CameraId} ({Ip}), mode={Mode}", cameraId, camera.IpAddress, mode);

        await _onvif.FactoryResetAsync(camera.ServiceUrl, camera.Credential.Username, password, mode, 15000);

        var job = new MaintenanceJob
        {
            JobType = "factory_reset",
            Status = JobStatus.Completed,
            CameraIds = JsonSerializer.Serialize(new[] { cameraId }),
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        _db.MaintenanceJobs.Add(job);
        await _db.SaveChangesAsync();
        if (_events != null) await _events.MaintenanceChangedAsync();
        return job;
    }

    public async Task<MaintenanceJob?> GetJobStatusAsync(int jobId)
    {
        return await _db.MaintenanceJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(job => job.Id == jobId);
    }

    private async Task<CollectedConfiguration> CollectConfigurationAsync(
        Camera camera,
        string password,
        string backupType,
        CancellationToken cancellationToken)
    {
        var root = new XElement(
            "ConfigBackup",
            new XAttribute("cameraId", camera.Id),
            new XAttribute("type", backupType),
            new XAttribute("timestamp", DateTime.UtcNow.ToString("O")));
        var failures = new List<(string Section, Exception Error)>();
        var collectedSections = 0;

        if (backupType is "full" or "network")
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var network = await _onvif.GetNetworkConfigAsync(camera.ServiceUrl, camera.Credential!.Username, password);
                root.Add(new XElement("NetworkConfig", JsonSerializer.Serialize(network)));
                collectedSections++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(("network", ex));
            }
        }

        if (backupType is "full" or "video_profiles")
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var profiles = await _onvif.GetProfilesAsync(camera.ServiceUrl, camera.Credential!.Username, password);
                var profilesElement = new XElement("MediaProfiles");
                foreach (var p in profiles)
                {
                    profilesElement.Add(new XElement(
                        "MediaProfile",
                        new XAttribute("token", p.Token),
                        JsonSerializer.Serialize(p)));
                }

                root.Add(profilesElement);
                collectedSections++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(("video_profiles", ex));
            }
        }

        if (backupType is "full" or "users")
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var users = await _onvif.GetUsersAsync(camera.ServiceUrl, camera.Credential!.Username, password);
                root.Add(new XElement("Users", JsonSerializer.Serialize(users)));
                collectedSections++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(("users", ex));
            }
        }

        if (collectedSections == 0)
        {
            var details = string.Join("; ", failures.Select(f => $"{f.Section}: {f.Error.Message}"));
            throw new InvalidOperationException($"Не удалось получить конфигурацию камеры. {details}");
        }

        if (failures.Count > 0)
        {
            root.Add(new XElement(
                "CollectionErrors",
                failures.Select(f => new XElement(
                    "Error",
                    new XAttribute("section", f.Section),
                    f.Error.Message))));

            foreach (var failure in failures)
            {
                _logger.LogWarning(
                    failure.Error,
                    "Configuration section {Section} could not be collected for camera {CameraId}",
                    failure.Section,
                    camera.Id);
            }
        }

        return new CollectedConfiguration(root.ToString(SaveOptions.DisableFormatting), failures.Count > 0);
    }

    private static string NormalizeBackupType(string backupType)
    {
        var normalized = backupType.Trim().ToLowerInvariant();
        return normalized is "full" or "network" or "video_profiles" or "users"
            ? normalized
            : throw new ArgumentException("Неизвестный тип резервной копии.", nameof(backupType));
    }

    private async Task<Camera> GetCameraWithCredential(int cameraId)
    {
        var camera = await _cameraRepo.GetByIdAsync(cameraId);
        if (camera == null)
            throw new ArgumentException($"Camera {cameraId} not found");
        if (camera.Credential == null)
            throw new InvalidOperationException($"No credentials for camera {cameraId}");
        return camera;
    }

    private sealed record CollectedConfiguration(string Xml, bool IsPartial);
}
