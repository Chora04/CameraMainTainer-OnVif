using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using CameraMaintainer.Core.Interfaces;

namespace CameraMaintainer.Web.Hubs;

/// <summary>Authenticated SignalR endpoint for server-side invalidation events.</summary>
public sealed class EventsHub : Hub
{
}

/// <summary>
/// Service wrapper for broadcasting events through the SignalR hub.
/// Injected into services/workers that need to send real-time updates.
/// </summary>
public sealed class EventBroadcaster : IMonitoringEvents
{
    private static readonly TimeSpan HealthBatchWindow = TimeSpan.FromMilliseconds(250);
    private readonly IHubContext<EventsHub> _hub;
    private readonly ILogger<EventBroadcaster> _logger;
    private readonly object _healthBatchLock = new();
    private readonly HashSet<int> _pendingHealthCameraIds = [];
    private bool _healthFlushScheduled;

    public EventBroadcaster(IHubContext<EventsHub> hub, ILogger<EventBroadcaster> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task HealthUpdatedAsync(int cameraId)
    {
        var scheduleFlush = false;
        lock (_healthBatchLock)
        {
            _pendingHealthCameraIds.Add(cameraId);
            if (!_healthFlushScheduled)
            {
                _healthFlushScheduled = true;
                scheduleFlush = true;
            }
        }

        if (scheduleFlush)
            _ = FlushHealthUpdatesAsync();

        return Task.CompletedTask;
    }

    public Task AlertsChangedAsync() => NotifyAsync("alerts.changed", new { });

    public Task WorkspaceChangedAsync(string[] topics) => NotifyAsync("workspace.changed", new { topics });
    public Task CameraRegistryChangedAsync() => WorkspaceChangedAsync(["cameras", "registry", "alerts", "navigation", "queue"]);
    public Task MaintenanceChangedAsync() => WorkspaceChangedAsync(["maintenance"]);

    private async Task FlushHealthUpdatesAsync()
    {
        await Task.Delay(HealthBatchWindow);

        int[] cameraIds;
        lock (_healthBatchLock)
        {
            cameraIds = _pendingHealthCameraIds.Order().ToArray();
            _pendingHealthCameraIds.Clear();
            _healthFlushScheduled = false;
        }

        if (cameraIds.Length > 0)
            await NotifyAsync("health.updated", new { cameraIds });
    }

    private async Task NotifyAsync(string eventType, object payload)
    {
        // Events invalidate authorized API data; a disconnected UI must not fail a saved check.
        try
        {
            await _hub.Clients.All.SendAsync(eventType, payload);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not publish {EventType}", eventType);
        }
    }

    public Task BroadcastAllAsync(string eventType, object payload) => NotifyAsync(eventType, payload);

    public async Task BroadcastUsersAsync(string eventType, object payload, IEnumerable<int> userIds)
    {
        var identifiers = userIds
            .Where(userId => userId > 0)
            .Distinct()
            .Select(userId => userId.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        if (identifiers.Length == 0)
            return;

        try
        {
            await _hub.Clients.Users(identifiers).SendAsync(eventType, payload);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not publish {EventType} to selected users", eventType);
        }
    }
}
