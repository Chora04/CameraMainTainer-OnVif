using Microsoft.Extensions.Caching.Memory;

namespace CameraMaintainer.Web.Services;

public readonly record struct SnapshotKey(int CameraId, string ServiceUrl, string Username, DateTime CredentialUpdatedAt);
public sealed record SnapshotFrame(byte[] Bytes, string ContentType, DateTimeOffset CapturedAt);

/// <summary>Bounded last-frame storage and coalesced, resource-limited preview downloads.</summary>
public sealed class SnapshotCache : IDisposable
{
    private sealed record Entry(SnapshotKey Key, SnapshotFrame Frame);
    private readonly object _sync = new();
    private readonly Dictionary<int, LinkedListNode<Entry>> _frames = new();
    private readonly LinkedList<Entry> _recent = new();
    private readonly MemoryCache _uris = new(new MemoryCacheOptions { SizeLimit = 1024 });
    private readonly SemaphoreSlim[] _cameraLocks = Enumerable.Range(0, 256).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly SemaphoreSlim _downloads;
    private readonly SemaphoreSlim _requests = new(64, 64);
    private readonly TimeProvider _clock;
    private readonly long _maximumBytes;
    private readonly TimeSpan _freshFor;
    private readonly TimeSpan _retainFor;
    private readonly TimeSpan _requestTimeout;
    private long _storedBytes;

    public SnapshotCache(long maximumBytes = 128 * 1024 * 1024, int concurrentDownloads = 4,
        TimeSpan? freshFor = null, TimeSpan? retainFor = null, TimeProvider? clock = null, TimeSpan? requestTimeout = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        _maximumBytes = maximumBytes;
        _downloads = new SemaphoreSlim(Math.Clamp(concurrentDownloads, 1, 8));
        _freshFor = freshFor ?? TimeSpan.FromSeconds(2);
        _retainFor = retainFor ?? TimeSpan.FromMinutes(30);
        _clock = clock ?? TimeProvider.System;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(25);
    }

    public long StoredBytes { get { lock (_sync) return _storedBytes; } }
    public int Count { get { lock (_sync) return _frames.Count; } }

    public SnapshotFrame? Get(SnapshotKey key)
    {
        lock (_sync)
        {
            if (!_frames.TryGetValue(key.CameraId, out var node)) return null;
            if (node.Value.Key != key || _clock.GetUtcNow() - node.Value.Frame.CapturedAt >= _retainFor)
            {
                RemoveNode(node);
                return null;
            }
            _recent.Remove(node);
            _recent.AddLast(node);
            return node.Value.Frame;
        }
    }

    public void Set(SnapshotKey key, SnapshotFrame frame)
    {
        lock (_sync)
        {
            if (_frames.TryGetValue(key.CameraId, out var previous)) RemoveNode(previous);
            if (frame.Bytes.LongLength > _maximumBytes) return;
            while (_recent.First != null && (_storedBytes + frame.Bytes.LongLength > _maximumBytes || _frames.Count >= 1024))
                RemoveNode(_recent.First);
            _frames[key.CameraId] = _recent.AddLast(new Entry(key, frame));
            _storedBytes += frame.Bytes.LongLength;
        }
    }

    public void Remove(int cameraId)
    {
        lock (_sync)
            if (_frames.TryGetValue(cameraId, out var node)) RemoveNode(node);
        _uris.Remove(cameraId);
    }

    private void RemoveNode(LinkedListNode<Entry> node)
    {
        _storedBytes -= node.Value.Frame.Bytes.LongLength;
        _frames.Remove(node.Value.Key.CameraId);
        _recent.Remove(node);
    }

    public string? GetUri(SnapshotKey key)
        => _uris.TryGetValue(key.CameraId, out (SnapshotKey Key, string Uri) cached) && cached.Key == key ? cached.Uri : null;

    public void SetUri(SnapshotKey key, string uri)
        => _uris.Set(key.CameraId, (key, uri), new MemoryCacheEntryOptions
        { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });

    public void RemoveUri(int cameraId) => _uris.Remove(cameraId);

    public async Task<SnapshotFrame> GetOrCreateAsync(SnapshotKey key,
        Func<CancellationToken, Task<(byte[] Bytes, string ContentType)>> download, CancellationToken cancellationToken = default)
    {
        bool Fresh(SnapshotFrame? value) => value != null && _clock.GetUtcNow() - value.CapturedAt < _freshFor;
        var frame = Get(key);
        if (Fresh(frame)) return frame!;
        if (!await _requests.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Snapshot download queue is busy.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);
            var token = timeout.Token;
            // Fixed stripes keep lock storage bounded even when millions of IDs are requested over time.
            var gate = _cameraLocks[(uint)key.CameraId % (uint)_cameraLocks.Length];
            await gate.WaitAsync(token);
            try
            {
                frame = Get(key);
                if (Fresh(frame)) return frame!;
                await _downloads.WaitAsync(token);
                try
                {
                    var result = await download(token);
                    frame = new SnapshotFrame(result.Bytes, result.ContentType, _clock.GetUtcNow());
                    Set(key, frame);
                    return frame;
                }
                finally { _downloads.Release(); }
            }
            finally { gate.Release(); }
        }
        finally { _requests.Release(); }
    }

    public void Dispose()
    {
        _uris.Dispose();
        _downloads.Dispose();
        _requests.Dispose();
        foreach (var gate in _cameraLocks) gate.Dispose();
        lock (_sync) { _frames.Clear(); _recent.Clear(); _storedBytes = 0; }
    }
}
