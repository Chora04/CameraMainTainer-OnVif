namespace CameraMaintainer.Services;

/// <summary>
/// Assigns items to a fixed number of workers in source order. A worker takes
/// the next item immediately after finishing, without waiting for other workers.
/// </summary>
public static class OrderedAsyncWorkerPool
{
    public static async Task RunAsync<T>(
        IReadOnlyList<T> items,
        int maxConcurrency,
        Func<T, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(operation);

        if (items.Count == 0)
            return;

        var nextIndex = -1;
        var workerCount = Math.Min(Math.Max(1, maxConcurrency), items.Count);

        async Task WorkerAsync()
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = Interlocked.Increment(ref nextIndex);
                if (index >= items.Count)
                    return;

                await operation(items[index], cancellationToken);
            }
        }

        var workers = new Task[workerCount];
        for (var index = 0; index < workerCount; index++)
            workers[index] = WorkerAsync();

        await Task.WhenAll(workers);
    }
}
