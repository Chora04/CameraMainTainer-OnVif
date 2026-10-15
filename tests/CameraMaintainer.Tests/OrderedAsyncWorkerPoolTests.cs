using System.Collections.Concurrent;
using CameraMaintainer.Services;

namespace CameraMaintainer.Tests;

public sealed class OrderedAsyncWorkerPoolTests
{
    [Fact]
    public async Task RunAsync_AssignsInOrderAndDoesNotWaitForSlowWorker()
    {
        var firstWorkerRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdCameraStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var claimed = new ConcurrentQueue<int>();

        var run = OrderedAsyncWorkerPool.RunAsync(
            new[] { 1, 2, 3, 4 },
            maxConcurrency: 2,
            async (cameraId, cancellationToken) =>
            {
                claimed.Enqueue(cameraId);
                if (cameraId == 1)
                    await firstWorkerRelease.Task.WaitAsync(cancellationToken);
                else if (cameraId == 2)
                    await Task.Delay(20, cancellationToken);
                else if (cameraId == 3)
                    thirdCameraStarted.TrySetResult();
            });

        await thirdCameraStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(run.IsCompleted);
        Assert.Equal(new[] { 1, 2, 3 }, claimed.Take(3));

        firstWorkerRelease.TrySetResult();
        await run;
        Assert.Equal(new[] { 1, 2, 3, 4 }, claimed.ToArray());
    }

    [Theory]
    [InlineData(50, 16)]
    [InlineData(1000, 8)]
    public async Task RunAsync_NeverExceedsConfiguredConcurrency(int count, int concurrency)
    {
        var active = 0;
        var peak = 0;
        var processed = new ConcurrentQueue<int>();

        await OrderedAsyncWorkerPool.RunAsync(
            Enumerable.Range(1, count).ToArray(),
            maxConcurrency: concurrency,
            async (cameraId, cancellationToken) =>
            {
                var current = Interlocked.Increment(ref active);
                InterlockedExtensions.Max(ref peak, current);
                processed.Enqueue(cameraId);
                await Task.Delay(5, cancellationToken);
                Interlocked.Decrement(ref active);
            });

        Assert.Equal(concurrency, peak);
        Assert.Equal(0, active);
        Assert.Equal(Enumerable.Range(1, count), processed.OrderBy(id => id));
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (value > current)
            {
                var previous = Interlocked.CompareExchange(ref target, value, current);
                if (previous == current)
                    return;
                current = previous;
            }
        }
    }
}
