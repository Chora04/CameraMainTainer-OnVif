using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Workers;

/// <summary>
/// Continuous health monitoring worker.
/// Checks all due cameras in a loop with configurable cycle interval.
/// Default: checks every 60 seconds which cameras are due, then processes them in parallel.
/// </summary>
public class HealthWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HealthWorker> _logger;
    private readonly TimeSpan _cycleInterval;

    public HealthWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<HealthWorker> logger,
        int cycleIntervalSeconds = 60)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cycleInterval = TimeSpan.FromSeconds(cycleIntervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HealthWorker started. Cycle interval: {Interval}s",
            _cycleInterval.TotalSeconds);

        try
        {
            // Let the operator-facing application start before the first scheduled batch.
            var startupDelay = _cycleInterval + TimeSpan.FromSeconds(Random.Shared.Next(0, 11));
            _logger.LogInformation("HealthWorker delaying first cycle by {Delay:F0}s", startupDelay.TotalSeconds);
            await Task.Delay(startupDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var healthService = scope.ServiceProvider.GetRequiredService<IHealthService>();

                    await healthService.CheckAllDueAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Health check cycle failed");
                }

                await DelayUntilNextCycleAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        _logger.LogInformation("HealthWorker stopped");
    }

    private async Task DelayUntilNextCycleAsync(CancellationToken stoppingToken)
    {
        var jitter = TimeSpan.FromSeconds(Random.Shared.Next(-5, 6));
        var delay = _cycleInterval + jitter;
        if (delay < TimeSpan.FromSeconds(1))
            delay = TimeSpan.FromSeconds(1);
        await Task.Delay(delay, stoppingToken);
    }
}
