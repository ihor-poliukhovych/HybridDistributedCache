namespace HybridDistributedCache;

/// <summary>
/// Shared storage for the data version of each cache. Redis in production,
/// an in-memory fake in tests.
/// </summary>
public interface ICacheVersionStore
{
    /// <summary>
    /// Returns the current version. When there is none yet, or it was lost, creates one with a
    /// new epoch and counter 0, so that every reset is seen as a new epoch.
    /// </summary>
    Task<CacheVersion> GetVersionAsync(string cacheName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments the counter, notifies the subscribers and returns the new version.
    /// </summary>
    Task<CacheVersion> IncrementVersionAsync(string cacheName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to version changes. Notifications are best effort: they can be lost
    /// during a reconnect, so callers must also check the version from time to time.
    /// </summary>
    Task<IAsyncDisposable> SubscribeAsync(
        string cacheName,
        Action<CacheVersion> onVersionChanged,
        CancellationToken cancellationToken = default);
}
