namespace HybridDistributedCache;

/// <summary>
/// A cache that keeps all values in the memory of the current process and
/// uses a shared version number to learn when other instances changed the data.
/// </summary>
/// <remarks>
/// The cache is not the source of truth. Write to your data store first, then call
/// <see cref="SetAsync"/>, <see cref="RemoveAsync"/> or <see cref="InvalidateAsync"/>
/// so that the other instances reload from the same data store.
/// </remarks>
public interface IHybridCacheManager<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    /// <summary>Returns the cached value, or <c>null</c> when the key is not in the data set.</summary>
    ValueTask<TValue?> GetAsync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>Returns a point-in-time copy of all cached values.</summary>
    ValueTask<IReadOnlyCollection<TValue>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Updates the local copy and tells the other instances to reload.</summary>
    Task SetAsync(TKey key, TValue value, CancellationToken cancellationToken = default);

    /// <summary>Removes the key from the local copy and tells the other instances to reload.</summary>
    Task RemoveAsync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>Tells every instance, this one included, to reload from the data source.</summary>
    Task InvalidateAsync(CancellationToken cancellationToken = default);
}
