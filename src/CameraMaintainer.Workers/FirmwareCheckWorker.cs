using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Workers;

/// <summary>
/// Periodic firmware version tracking worker.
/// Runs daily — records firmware versions, compares against baselines,
/// and generates alerts for outdated or changed firmware.
/// </summary>
public class FirmwareCheckWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FirmwareCheckWorker> _logger;

    public FirmwareCheckWorker(IServiceScopeFactory scopeFactory, ILogger<FirmwareCheckWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FirmwareCheckWorker started");

        try
        {
            // Wait 10 minutes on startup
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var afterId = 0;
                    var cameraCount = 0;
                    var updatesDetected = 0;
                    var outdatedCount = 0;
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        // Release tracked firmware/alert entities between batches; no fixed fleet-size cutoff.
                        using var scope = _scopeFactory.CreateScope();
                        var cameraRepo = scope.ServiceProvider.GetRequiredService<ICameraRepository>();
                        var firmwareService = scope.ServiceProvider.GetRequiredService<IFirmwareService>();
                        var alertService = scope.ServiceProvider.GetRequiredService<IAlertService>();

                        var cameras = await cameraRepo.GetAllAsync(new CameraFilter
                        {
                            Status = CameraStatus.Online.ToString(),
                            PerPage = 100,
                            SortBy = "id",
                            AfterId = afterId
                        });
                        if (cameras.Count == 0) break;
                        afterId = cameras[^1].Id;
                        cameraCount += cameras.Count;

                        foreach (var camera in cameras)
                        {
                            if (stoppingToken.IsCancellationRequested) break;

                            try
                            {
                                // Skip cameras without firmware info
                                if (string.IsNullOrEmpty(camera.FirmwareVersion) ||
                                    camera.FirmwareVersion == "Unknown")
                                {
                                    // Only alert once — check if alert already exists
                                    await alertService.CreateAlertAsync(
                                        camera.Id,
                                        AlertType.FirmwareOutdated,
                                        AlertSeverity.Info,
                                        $"Unable to determine firmware version for {camera.Name}",
                                        "Camera may have non-standard ONVIF implementation or firmware reporting is disabled."
                                    );
                                    continue;
                                }

                                // Record current version (creates record only on change)
                                var record = await firmwareService.RecordVersionAsync(
                                    camera.Id,
                                    camera.FirmwareVersion,
                                    "Unknown", // Hardware ID is unavailable here and must not drive version changes.
                                    camera.Manufacturer ?? "Unknown",
                                    camera.Model ?? "Unknown");

                                if (record != null)
                                {
                                    updatesDetected++;

                                    // Create an alert only when previous and current versions really differ.
                                    var history = await firmwareService.GetHistoryAsync(camera.Id, limit: 2);
                                    if (history.Count >= 2)
                                    {
                                        var prevVersion = history[1].FirmwareVersion;
                                        if (!FirmwareVersionsEqual(prevVersion, record.FirmwareVersion))
                                        {
                                            _logger.LogWarning(
                                                "Firmware change for camera {CameraId} ({Name}): {PrevVer} → {NewVer}",
                                                camera.Id, camera.Name, prevVersion, camera.FirmwareVersion);

                                            await alertService.CreateAlertAsync(
                                                camera.Id,
                                                AlertType.FirmwareOutdated,
                                                AlertSeverity.Warning,
                                                $"Firmware changed on {camera.Name}: {prevVersion} → {camera.FirmwareVersion}",
                                                $"Manufacturer: {camera.Manufacturer}, Model: {camera.Model}"
                                            );
                                        }
                                    }
                                }

                                // Check if firmware is up-to-date against baseline
                                var isCurrent = record?.IsCurrent ?? firmwareService.IsUpToDate(
                                    camera.Manufacturer ?? "Unknown",
                                    camera.Model ?? "Unknown",
                                    camera.FirmwareVersion);
                                if (!isCurrent)
                                {
                                    outdatedCount++;
                                    await alertService.CreateAlertAsync(
                                        camera.Id,
                                        AlertType.FirmwareOutdated,
                                        AlertSeverity.Warning,
                                        $"Firmware outdated on {camera.Name}: {camera.FirmwareVersion}",
                                        $"Manufacturer: {camera.Manufacturer}, Model: {camera.Model}. " +
                                        "Check manufacturer for latest firmware."
                                    );
                                }
                            }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Firmware check failed for camera {CameraId}", camera.Id);
                            }
                        }

                    }
                    _logger.LogInformation(
                        "Firmware check completed: {CameraCount} cameras checked, " +
                        "{Updates} firmware updates detected, {Outdated} outdated",
                        cameraCount, updatesDetected, outdatedCount);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Firmware check cycle failed");
                }

                // Run daily
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        _logger.LogInformation("FirmwareCheckWorker stopped");
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
