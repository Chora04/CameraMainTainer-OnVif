using CameraMaintainer.Infrastructure.CircuitBreaker;

namespace CameraMaintainer.Tests;

public sealed class CircuitBreakerTests
{
    [Fact]
    public void RecordFailure_OpensCircuitAtThreshold()
    {
        var breaker = new CircuitBreaker(failureThreshold: 2, cooldownSeconds: 60);

        breaker.RecordFailure(1);
        Assert.Equal(CircuitState.Closed, breaker.GetState(1));

        breaker.RecordFailure(1);

        Assert.Equal(CircuitState.Open, breaker.GetState(1));
        Assert.Equal(2, breaker.GetFailureCount(1));
        Assert.False(breaker.AllowCall(1));
    }

    [Fact]
    public void RecordFailure_FirstOpenUsesBaseCooldown()
    {
        var breaker = new CircuitBreaker(
            failureThreshold: 2,
            cooldownSeconds: 120,
            backoffMultiplier: 1.5);

        breaker.RecordFailure(1);
        breaker.RecordFailure(1);

        Assert.Equal(120, breaker.GetCurrentCooldownSeconds(1));
        breaker.RecordFailure(1);
        Assert.Equal(180, breaker.GetCurrentCooldownSeconds(1));
    }

    [Fact]
    public void HalfOpen_AllowsOnlyOneProbe()
    {
        var breaker = new CircuitBreaker(failureThreshold: 1, cooldownSeconds: 0);
        breaker.RecordFailure(1);

        Assert.True(breaker.AllowCall(1));
        Assert.Equal(CircuitState.HalfOpen, breaker.GetState(1));
        Assert.False(breaker.AllowCall(1));
    }

    [Fact]
    public async Task HalfOpen_WithConcurrentChecks_AllowsExactlyOneProbe()
    {
        var breaker = new CircuitBreaker(failureThreshold: 1, cooldownSeconds: 0);
        breaker.RecordFailure(1);
        using var start = new ManualResetEventSlim(false);

        var checks = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                return breaker.AllowCall(1);
            }))
            .ToArray();

        start.Set();
        var results = await Task.WhenAll(checks);

        Assert.Single(results, allowed => allowed);
        Assert.Equal(CircuitState.HalfOpen, breaker.GetState(1));
    }

    [Fact]
    public void RecordSuccess_ClosesCircuitAndClearsFailures()
    {
        var breaker = new CircuitBreaker(failureThreshold: 1, cooldownSeconds: 0);
        breaker.RecordFailure(1);
        Assert.True(breaker.AllowCall(1));

        breaker.RecordSuccess(1);

        Assert.Equal(CircuitState.Closed, breaker.GetState(1));
        Assert.Equal(0, breaker.GetFailureCount(1));
        Assert.True(breaker.AllowCall(1));
    }

    [Fact]
    public void Cameras_HaveIndependentCircuitState()
    {
        var breaker = new CircuitBreaker(failureThreshold: 1, cooldownSeconds: 60);

        breaker.RecordFailure(7);

        Assert.False(breaker.AllowCall(7));
        Assert.True(breaker.AllowCall(8));
        Assert.Equal(CircuitState.Closed, breaker.GetState(8));
    }
}
