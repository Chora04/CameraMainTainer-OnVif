using CameraMaintainer.Services;
using CameraMaintainer.Core.Models;

namespace CameraMaintainer.Tests;

public sealed class HealthCheckConcurrencyGateTests
{
    [Fact]
    public async Task QueuePasses_DoNotOverlapAndReleaseGateAfterFailure()
    {
        using var gate = new HealthCheckConcurrencyGate(8);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = false;
        var first = gate.RunCycleAsync(async () => { started.SetResult(); await release.Task; });
        await started.Task;
        var second = gate.RunCycleAsync(() => { secondStarted = true; return Task.CompletedTask; });
        Assert.False(secondStarted);
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.True(secondStarted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunCycleAsync(() => throw new InvalidOperationException()));
        await gate.RunCycleAsync(() => Task.CompletedTask);
    }

    [Fact]
    public async Task RunAsync_NeverExceedsConfiguredConcurrency()
    {
        using var gate = new HealthCheckConcurrencyGate(2);
        var current = 0;
        var peak = 0;

        var tasks = Enumerable.Range(0, 10).Select(index => gate.RunAsync(async () =>
        {
            var running = Interlocked.Increment(ref current);
            UpdatePeak(ref peak, running);
            try
            {
                await Task.Delay(25);
                return index;
            }
            finally
            {
                Interlocked.Decrement(ref current);
            }
        }));

        var results = await Task.WhenAll(tasks);

        Assert.Equal(Enumerable.Range(0, 10), results);
        Assert.Equal(2, peak);
    }

    [Fact]
    public async Task RunCameraAsync_JoinsConcurrentChecksForSameCamera()
    {
        using var gate = new HealthCheckConcurrencyGate(2);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;

        async Task<HealthRecord> CheckCamera()
        {
            Interlocked.Increment(ref executions);
            started.SetResult();
            await release.Task;
            return new HealthRecord { CameraId = 35, StatusCode = "ok" };
        }

        var first = gate.RunCameraAsync(35, CheckCamera);
        await started.Task;
        Assert.Equal(1, gate.ActiveChecks);
        Assert.Equal(1, gate.InFlightChecks);
        var second = gate.RunCameraAsync(35, CheckCamera);
        release.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, executions);
        Assert.Same(results[0], results[1]);
        Assert.Equal(0, gate.ActiveChecks);
    }

    private static void UpdatePeak(ref int peak, int candidate)
    {
        while (true)
        {
            var currentPeak = Volatile.Read(ref peak);
            if (candidate <= currentPeak ||
                Interlocked.CompareExchange(ref peak, candidate, currentPeak) == currentPeak)
            {
                return;
            }
        }
    }
}
