using System.Collections.Concurrent;
using CameraMaintainer.Core.Models;

namespace CameraMaintainer.Services;

/// <summary>
/// Applies one process-wide concurrency limit to every camera check,
/// and joins concurrent requests for the same camera into one operation.
/// </summary>
public sealed class HealthCheckConcurrencyGate : IDisposable
{
    private readonly ConcurrentDictionary<int, Lazy<Task<HealthRecord>>> _inFlightChecks = new();
    private readonly SemaphoreSlim _semaphore;
    private readonly SemaphoreSlim _cycleSemaphore = new(1, 1);

    public HealthCheckConcurrencyGate(int maxConcurrency)
    {
        MaxConcurrency = Math.Max(1, maxConcurrency);
        _semaphore = new SemaphoreSlim(MaxConcurrency);
    }

    public int MaxConcurrency { get; }
    public int ActiveChecks => Math.Clamp(MaxConcurrency - _semaphore.CurrentCount, 0, MaxConcurrency);
    public int InFlightChecks => _inFlightChecks.Count;

    public async Task RunCycleAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        await _cycleSemaphore.WaitAsync(cancellationToken);
        try { await operation(); }
        finally { _cycleSemaphore.Release(); }
    }

    public Task<HealthRecord> RunCameraAsync(
        int cameraId,
        Func<Task<HealthRecord>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cameraId);
        ArgumentNullException.ThrowIfNull(operation);

        var candidate = new Lazy<Task<HealthRecord>>(
            () => RunAsync(operation, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var active = _inFlightChecks.GetOrAdd(cameraId, candidate);

        if (ReferenceEquals(active, candidate))
        {
            _ = active.Value.ContinueWith(
                _ => RemoveCompletedCheck(cameraId, active),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return cancellationToken.CanBeCanceled
            ? active.Value.WaitAsync(cancellationToken)
            : active.Value;
    }

    public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void RemoveCompletedCheck(int cameraId, Lazy<Task<HealthRecord>> completed)
    {
        if (_inFlightChecks.TryGetValue(cameraId, out var current) && ReferenceEquals(current, completed))
            _inFlightChecks.TryRemove(cameraId, out _);
    }

    public void Dispose()
    {
        _semaphore.Dispose();
        _cycleSemaphore.Dispose();
    }
}
