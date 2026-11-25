using System.Collections.Concurrent;

namespace HybridDistributedCache.Tests.Fakes;

/// <summary>
/// Plays the role of Redis for several cache instances in one test.
/// Notifications are delivered synchronously, unless they are switched off to simulate losing them.
/// </summary>
internal sealed class InMemoryCacheVersionStore : ICacheVersionStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, CacheVersion> _versions = [];
    private readonly ConcurrentDictionary<Guid, (string CacheName, Action<CacheVersion> Handler)> _subscribers = new();
    private int _getVersionCalls;

    public bool DeliverNotifications { get; set; } = true;

    public bool IsAvailable { get; set; } = true;

    public int GetVersionCalls => Volatile.Read(ref _getVersionCalls);

    public Task<CacheVersion> GetVersionAsync(string cacheName, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _getVersionCalls);
        ThrowIfUnavailable();

        lock (_lock)
        {
            return Task.FromResult(GetOrCreate(cacheName));
        }
    }

    public Task<CacheVersion> IncrementVersionAsync(string cacheName, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();

        CacheVersion version;
        lock (_lock)
        {
            var current = GetOrCreate(cacheName);
            version = current with { Counter = current.Counter + 1 };
            _versions[cacheName] = version;
        }

        if (DeliverNotifications)
        {
            foreach (var (name, handler) in _subscribers.Values)
            {
                if (name == cacheName)
                {
                    handler(version);
                }
            }
        }

        return Task.FromResult(version);
    }

    public Task<IAsyncDisposable> SubscribeAsync(
        string cacheName,
        Action<CacheVersion> onVersionChanged,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        var id = Guid.NewGuid();
        _subscribers[id] = (cacheName, onVersionChanged);
        return Task.FromResult<IAsyncDisposable>(new Unsubscriber(() => _subscribers.TryRemove(id, out _)));
    }

    /// <summary>Simulates FLUSHALL or eviction of the version key.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _versions.Clear();
        }
    }

    /// <summary>Mirrors HSETNX on a missing key. Must be called while holding the lock.</summary>
    private CacheVersion GetOrCreate(string cacheName)
    {
        if (!_versions.TryGetValue(cacheName, out var version))
        {
            version = new CacheVersion(Guid.NewGuid().ToString("N"), 0);
            _versions[cacheName] = version;
        }

        return version;
    }

    private void ThrowIfUnavailable()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("Version store is unavailable.");
        }
    }

    private sealed class Unsubscriber(Action unsubscribe) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            unsubscribe();
            return ValueTask.CompletedTask;
        }
    }
}
