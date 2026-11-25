namespace HybridDistributedCache;

/// <summary>
/// Loads the full data set from the source of truth (a database, a file, a service).
/// </summary>
/// <remarks>
/// Registered as a singleton. If loading needs scoped services such as a DbContext,
/// create a scope inside <see cref="LoadAllAsync"/> with <c>IServiceScopeFactory</c>.
/// </remarks>
public interface ICacheDataSource<TKey, TValue>
    where TKey : notnull
{
    Task<IReadOnlyDictionary<TKey, TValue>> LoadAllAsync(CancellationToken cancellationToken);
}
