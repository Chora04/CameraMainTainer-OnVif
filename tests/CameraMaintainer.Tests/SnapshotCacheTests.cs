using CameraMaintainer.Web.Services;

namespace CameraMaintainer.Tests;

public sealed class SnapshotCacheTests
{
    private static SnapshotKey Key(int id) => new(id, $"http://camera-{id}/onvif", "user", DateTime.UnixEpoch);
    private static SnapshotFrame Frame(int bytes, DateTimeOffset? at = null)
        => new(new byte[bytes], "image/jpeg", at ?? DateTimeOffset.UtcNow);

    [Fact]
    public void MemoryBudget_EvictsLeastRecentlyUsedAndNeverGrowsWithFleetSize()
    {
        using var cache = new SnapshotCache(maximumBytes: 1000);
        cache.Set(Key(1), Frame(400));
        cache.Set(Key(2), Frame(400));
        Assert.NotNull(cache.Get(Key(1)));
        cache.Set(Key(3), Frame(400));
        Assert.Null(cache.Get(Key(2)));
        Assert.NotNull(cache.Get(Key(1)));
        for (var id = 4; id <= 20_000; id++) cache.Set(Key(id), Frame(400));
        Assert.Equal(800, cache.StoredBytes);
        Assert.Equal(2, cache.Count);
        Assert.NotNull(cache.Get(Key(20_000)));
        cache.Set(Key(20_000), Frame(1500));
        Assert.Equal(400, cache.StoredBytes);
        Assert.Null(cache.Get(Key(20_000)));
    }

    [Fact]
    public void ExpirationCredentialChangesAndDelete_DoNotReturnOldFramesOrUris()
    {
        var clock = new TestClock();
        using var cache = new SnapshotCache(clock: clock, retainFor: TimeSpan.FromMinutes(30));
        var key = Key(1);
        cache.Set(key, Frame(100, clock.GetUtcNow()));
        cache.SetUri(key, "http://camera/snapshot");
        var changed = key with { CredentialUpdatedAt = DateTime.UnixEpoch.AddSeconds(1) };
        Assert.Null(cache.Get(changed));
        Assert.Null(cache.GetUri(changed));
        cache.Set(key, Frame(100, clock.GetUtcNow()));
        clock.Now += TimeSpan.FromMinutes(30);
        Assert.Null(cache.Get(key));
        Assert.Equal(0, cache.StoredBytes);
        cache.Set(key, Frame(100, clock.GetUtcNow()));
        cache.Remove(1);
        Assert.Null(cache.Get(key));
        Assert.Null(cache.GetUri(key));
    }

    [Fact]
    public async Task SimultaneousViewers_DownloadOneFrameAndRefreshAfterFreshnessWindow()
    {
        var clock = new TestClock();
        using var cache = new SnapshotCache(clock: clock);
        var calls = 0;
        async Task<(byte[], string)> Download(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(20, ct);
            return (new byte[100], "image/jpeg");
        }
        var frames = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => cache.GetOrCreateAsync(Key(1), Download)));
        Assert.Equal(1, calls);
        Assert.All(frames, frame => Assert.Same(frames[0], frame));
        clock.Now += TimeSpan.FromSeconds(3);
        await cache.GetOrCreateAsync(Key(1), Download);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Downloads_AreLimitedAndCancellationDoesNotPoisonNextRequest()
    {
        using var cache = new SnapshotCache(concurrentDownloads: 2);
        var active = 0;
        var peak = 0;
        var sync = new object();
        async Task<(byte[], string)> Download(CancellationToken ct)
        {
            lock (sync) peak = Math.Max(peak, ++active);
            try { await Task.Delay(10, ct); return (new byte[100], "image/jpeg"); }
            finally { lock (sync) active--; }
        }
        await Task.WhenAll(Enumerable.Range(1, 40).Select(id => cache.GetOrCreateAsync(Key(id), Download)));
        Assert.Equal(2, peak);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetOrCreateAsync(Key(41), Download, canceled.Token));
        Assert.NotNull(await cache.GetOrCreateAsync(Key(41), Download));
    }

    [Fact]
    public async Task FailedDownload_KeepsLastFrameForExplicitStaleFallback()
    {
        using var cache = new SnapshotCache();
        var previous = Frame(100, DateTimeOffset.UtcNow.AddMinutes(-1));
        cache.Set(Key(1), previous);
        await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetOrCreateAsync(Key(1), _ => throw new HttpRequestException("offline")));
        Assert.Same(previous, cache.Get(Key(1)));
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task StalledBody_TimesOutAndReleasesDownloadSlot()
    {
        using var cache = new SnapshotCache(concurrentDownloads: 1, requestTimeout: TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetOrCreateAsync(Key(1), async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return (Array.Empty<byte>(), "image/jpeg");
        }));
        Assert.NotNull(await cache.GetOrCreateAsync(Key(1), _ => Task.FromResult((new byte[100], "image/jpeg"))));
    }

    [Fact]
    public async Task AdmissionLimit_RejectsExcessWaitersWithoutGrowingTheQueue()
    {
        using var cache = new SnapshotCache(concurrentDownloads: 1);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Enumerable.Range(1, 64).Select(id => cache.GetOrCreateAsync(Key(id), async ct =>
        {
            await release.Task.WaitAsync(ct);
            return (new byte[10], "image/jpeg");
        })).ToArray();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrCreateAsync(Key(65),
                _ => Task.FromResult((new byte[10], "image/jpeg"))));
        }
        finally { release.TrySetResult(); await Task.WhenAll(pending); }
    }
}
