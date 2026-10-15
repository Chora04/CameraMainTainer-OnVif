using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Workers;

/// <summary>
/// Automatic configuration backup worker.
/// Runs daily — backs up configs for all online cameras that have auto_backup enabled.
/// Also backs up newly discovered cameras on first check.
/// </summary>
public class BackupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackupWorker> _logger;
    private readonly int _maxConcurrency;

    public BackupWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<BackupWorker> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _maxConcurrency = Math.Clamp(configuration.GetValue("Backup:Concurrency", 4), 1, 16);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BackupWorker started");

        try
        {
            // Wait 5 minutes on startup
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    List<int> cameraIds;
                    await using (var lookupScope = _scopeFactory.CreateAsyncScope())
                    {
                        var cameraRepo = lookupScope.ServiceProvider.GetRequiredService<ICameraRepository>();
                        cameraIds = await cameraRepo.GetCameraIdsDueForBackupAsync(
                            DateTime.UtcNow.AddDays(-7));
                    }

                    var backupCount = 0;
                    var partialCount = 0;
                    await OrderedAsyncWorkerPool.RunAsync(
                        cameraIds,
                        _maxConcurrency,
                        async (cameraId, cancellationToken) =>
                        {
                            try
                            {
                                await using var cameraScope = _scopeFactory.CreateAsyncScope();
                                var maintService = cameraScope.ServiceProvider.GetRequiredService<IMaintenanceService>();
                                var backup = await maintService.BackupConfigAsync(
                                    cameraId,
                                    "full",
                                    "automatic",
                                    cancellationToken);
                                Interlocked.Increment(ref backupCount);
                                if (backup.BackupType.EndsWith("_partial", StringComparison.Ordinal))
                                    Interlocked.Increment(ref partialCount);
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Auto-backup failed for camera {CameraId}", cameraId);
                            }
                        },
                        stoppingToken);

                    _logger.LogInformation(
                        "Backup cycle completed: {Count} cameras backed up, {PartialCount} partial",
                        backupCount,
                        partialCount);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Backup cycle failed");
                }

                // Run daily
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        _logger.LogInformation("BackupWorker stopped");
    }
}
