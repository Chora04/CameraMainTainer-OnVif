using CameraMaintainer.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Workers;

public sealed class AlertRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlertRetentionWorker> _logger;

    public AlertRetentionWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<AlertRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<AlertRetentionService>();
                    var result = await service.CleanupAsync(DateTime.UtcNow, stoppingToken);

                    _logger.LogInformation(
                        "Alert retention completed: {DeletedAlerts} alerts and {DeletedAuditEvents} audit events removed; cutoff {CutoffUtc}",
                        result.DeletedAlerts,
                        result.DeletedAuditEvents,
                        result.CutoffUtc);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Alert retention cycle failed");
                }

                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
