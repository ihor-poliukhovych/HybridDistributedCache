using System.Collections.Concurrent;

namespace HybridDistributedCache.Tests.Fakes;

/// <summary>
/// Plays the role of Redis for several cache instances in one test.
/// Notifications are delivered synchronously, unless they are switched off to simulate losing them.
/// </summary>
internal sealed class InMemoryCacheVersionStore : ICacheVersionStore
{
    private readonly ConcurrentDictionary<string, long> _versions = new();
    private readonly ConcurrentDictionary<Guid, (string CacheName, Action<long> Handler)> _subscribers = new();
    private int _getVersionCalls;

    public bool DeliverNotifications { get; set; } = true;

    public bool IsAvailable { get; set; } = true;

    public int GetVersionCalls => Volatile.Read(ref _getVersionCalls);

    public Task<long> GetVersionAsync(string cacheName, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _getVersionCalls);
        ThrowIfUnavailable();
        return Task.FromResult(_versions.GetValueOrDefault(cacheName));
    }

    public Task<long> IncrementVersionAsync(string cacheName, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        var version = _versions.AddOrUpdate(cacheName, 1, (_, current) => current + 1);

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
        Action<long> onVersionChanged,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        var id = Guid.NewGuid();
        _subscribers[id] = (cacheName, onVersionChanged);
        return Task.FromResult<IAsyncDisposable>(new Unsubscriber(() => _subscribers.TryRemove(id, out _)));
    }

    /// <summary>Simulates FLUSHALL or eviction of the version key.</summary>
    public void Reset() => _versions.Clear();

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
