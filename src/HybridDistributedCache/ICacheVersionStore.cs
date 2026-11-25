namespace HybridDistributedCache;

/// <summary>
/// Shared storage for the data version of each cache. Redis in production,
/// an in-memory fake in tests.
/// </summary>
public interface ICacheVersionStore
{
    /// <summary>Returns the current version, or 0 when no change was recorded yet.</summary>
    Task<long> GetVersionAsync(string cacheName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments the version, notifies the subscribers and returns the new version.
    /// </summary>
    Task<long> IncrementVersionAsync(string cacheName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to version changes. Notifications are best effort: they can be lost
    /// during a reconnect, so callers must also check the version from time to time.
    /// </summary>
    Task<IAsyncDisposable> SubscribeAsync(
        string cacheName,
        Action<long> onVersionChanged,
        CancellationToken cancellationToken = default);
}
