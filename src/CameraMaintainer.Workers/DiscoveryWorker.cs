using CameraMaintainer.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Workers;

/// <summary>
/// Periodic background worker that scans the network for new ONVIF cameras.
/// Runs on a configurable interval (default: every 5 minutes).
/// </summary>
public class DiscoveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DiscoveryWorker> _logger;
    private readonly TimeSpan _interval;

    public DiscoveryWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<DiscoveryWorker> logger,
        int intervalMinutes = 5)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromMinutes(intervalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DiscoveryWorker started. Interval: {Interval}min", _interval.TotalMinutes);

        try
        {
            // Wait a bit on startup to let everything initialize
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Starting periodic discovery scan...");

                    using var scope = _scopeFactory.CreateScope();
                    var discoveryService = scope.ServiceProvider.GetRequiredService<IDiscoveryService>();

                    var scan = await discoveryService.ScanAsync(
                        "ws_discovery",
                        cancellationToken: stoppingToken);

                    _logger.LogInformation(
                        "Discovery scan completed: {Found} cameras found, {Added} new cameras added",
                        scan.CamerasFound, scan.CamerasAdded);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Discovery scan failed");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        _logger.LogInformation("DiscoveryWorker stopped");
    }
}
