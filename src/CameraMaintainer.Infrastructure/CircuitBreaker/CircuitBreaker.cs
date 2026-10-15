using System.Collections.Concurrent;

namespace CameraMaintainer.Infrastructure.CircuitBreaker;

public enum CircuitState
{
    Closed,    // Normal operation
    Open,      // Skip calls — too many failures
    HalfOpen   // Allow one probe after cooldown
}

public class CircuitBreaker
{
    private readonly ConcurrentDictionary<int, CircuitState> _states = new();
    private readonly ConcurrentDictionary<int, int> _failures = new();
    private readonly ConcurrentDictionary<int, long> _lastFailureTicks = new();
    private readonly ConcurrentDictionary<int, double> _currentCooldowns = new();

    private readonly int _failureThreshold;
    private readonly int _baseCooldownSeconds;
    private readonly double _backoffMultiplier;
    private readonly int _maxCooldownSeconds;

    public CircuitBreaker(int failureThreshold = 3, int cooldownSeconds = 120,
        double backoffMultiplier = 1.5, int maxCooldownSeconds = 1800)
    {
        _failureThreshold = failureThreshold;
        _baseCooldownSeconds = cooldownSeconds;
        _backoffMultiplier = backoffMultiplier;
        _maxCooldownSeconds = maxCooldownSeconds;
    }

    public bool AllowCall(int cameraId)
    {
        var state = _states.GetOrAdd(cameraId, CircuitState.Closed);

        if (state == CircuitState.Closed)
            return true;

        if (state == CircuitState.HalfOpen)
            return false;

        // Open — check cooldown (with exponential backoff)
        if (_lastFailureTicks.TryGetValue(cameraId, out var lastTicks))
        {
            var currentCooldown = _currentCooldowns.GetOrAdd(cameraId, _baseCooldownSeconds);
            var elapsed = (DateTime.UtcNow.Ticks - lastTicks) / TimeSpan.TicksPerSecond;
            if (elapsed >= currentCooldown &&
                _states.TryUpdate(cameraId, CircuitState.HalfOpen, CircuitState.Open))
            {
                return true;
            }
        }

        return false;
    }

    public void RecordSuccess(int cameraId)
    {
        _states[cameraId] = CircuitState.Closed;
        _failures[cameraId] = 0;
        _currentCooldowns[cameraId] = _baseCooldownSeconds; // Reset to base cooldown
    }

    public void RecordFailure(int cameraId)
    {
        var current = _failures.AddOrUpdate(cameraId, 1, (_, v) => v + 1);
        _lastFailureTicks[cameraId] = DateTime.UtcNow.Ticks;

        if (current >= _failureThreshold)
        {
            _states[cameraId] = CircuitState.Open;

            // Exponential backoff: multiply cooldown for each additional failure
            var extraFailures = current - _failureThreshold;
            var newCooldown = _baseCooldownSeconds * Math.Pow(_backoffMultiplier, extraFailures);
            _currentCooldowns[cameraId] = Math.Min(newCooldown, _maxCooldownSeconds);
        }
    }

    public void Reset(int cameraId)
    {
        _states.TryRemove(cameraId, out _);
        _failures.TryRemove(cameraId, out _);
        _lastFailureTicks.TryRemove(cameraId, out _);
        _currentCooldowns.TryRemove(cameraId, out _);
    }

    public CircuitState GetState(int cameraId) => _states.GetOrAdd(cameraId, CircuitState.Closed);
    public int GetFailureCount(int cameraId) => _failures.GetOrAdd(cameraId, 0);
    public double GetCurrentCooldownSeconds(int cameraId) =>
        _currentCooldowns.GetOrAdd(cameraId, _baseCooldownSeconds);
}
