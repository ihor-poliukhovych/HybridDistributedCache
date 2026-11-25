using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HybridDistributedCache;

/// <summary>
/// Keeps the whole data set in local memory and reloads it when the shared version changes.
/// </summary>
/// <remarks>
/// <para>Reads are served from memory. The version store is asked for the current version at most
/// once per <see cref="HybridCacheOptions.VersionCheckInterval"/>; Pub/Sub notifications make
/// other instances notice a change earlier.</para>
/// <para>Loads, version checks and local writes run one at a time behind a single gate, so a burst
/// of reads after a change triggers exactly one reload.</para>
/// <para>If a reload fails, the previous data set is served and the data source is not called
/// again before <see cref="HybridCacheOptions.ReloadRetryInterval"/> has passed.</para>
/// </remarks>
public sealed class HybridCacheManager<TKey, TValue> : IHybridCacheManager<TKey, TValue>, IAsyncDisposable
    where TKey : notnull
    where TValue : class
{
    /// <summary>The version is unknown because the version store could not be reached.</summary>
    private const long UnknownVersion = -1;

    private readonly ICacheDataSource<TKey, TValue> _dataSource;
    private readonly ICacheVersionStore _versionStore;
    private readonly HybridCacheOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly string _cacheName;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile Snapshot? _snapshot;
    private long _latestKnownVersion = UnknownVersion;
    private long _nextVersionCheckTicks;
    private long _nextReloadAttemptTicks;
    private IAsyncDisposable? _subscription;
    private bool _disposed;

    public HybridCacheManager(
        ICacheDataSource<TKey, TValue> dataSource,
        ICacheVersionStore versionStore,
        HybridCacheOptions options,
        TimeProvider? timeProvider = null,
        ILogger<HybridCacheManager<TKey, TValue>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(versionStore);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _dataSource = dataSource;
        _versionStore = versionStore;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<HybridCacheManager<TKey, TValue>>.Instance;
        _cacheName = string.IsNullOrWhiteSpace(options.CacheName) ? typeof(TValue).FullName! : options.CacheName;
    }

    public async ValueTask<TValue?> GetAsync(TKey key, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Items.TryGetValue(key, out var value) ? value : null;
    }

    public async ValueTask<IReadOnlyCollection<TValue>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Items.Values.ToArray();
    }

    public Task SetAsync(TKey key, TValue value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ChangeAsync(items => items[key] = value, cancellationToken);
    }

    public Task RemoveAsync(TKey key, CancellationToken cancellationToken = default) =>
        ChangeAsync(items => items.TryRemove(key, out _), cancellationToken);

    public async Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var version = await _versionStore.IncrementVersionAsync(_cacheName, cancellationToken).ConfigureAwait(false);

            // The local snapshot stays older than the new version, so the next read reloads it.
            ObserveVersion(version);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }

    private async Task ChangeAsync(Action<ConcurrentDictionary<TKey, TValue>> change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Apply locally first, so this instance serves the new value even if the version store fails.
            var snapshot = _snapshot;
            if (snapshot is not null)
            {
                change(snapshot.Items);
            }

            var version = await _versionStore.IncrementVersionAsync(_cacheName, cancellationToken).ConfigureAwait(false);

            // If nobody else wrote since our last load, the local copy now matches the new version.
            // Otherwise we missed someone's change: keep the old version so the next read reloads.
            if (snapshot is not null && snapshot.Version != UnknownVersion && version == snapshot.Version + 1)
            {
                _snapshot = snapshot with { Version = version };
            }

            ObserveVersion(version);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<Snapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = _snapshot;
        // A stale copy is served as is while a failed reload waits for its retry.
        if (snapshot is not null && (!IsStale(snapshot) || !IsReloadDue()))
        {
            if (!IsVersionCheckDue())
            {
                return snapshot;
            }

            // Only one reader checks the version. Everybody else keeps reading the current data.
            if (!_gate.Wait(0))
            {
                return snapshot;
            }

            try
            {
                if (IsVersionCheckDue())
                {
                    await CheckVersionAsync(cancellationToken).ConfigureAwait(false);
                }

                if (IsStale(_snapshot!) && IsReloadDue())
                {
                    await ReloadAsync(cancellationToken).ConfigureAwait(false);
                }

                return _snapshot!;
            }
            finally
            {
                _gate.Release();
            }
        }

        // No data yet, or we know it is out of date: wait for a single reload.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            snapshot = _snapshot;
            if (snapshot is null || (IsStale(snapshot) && IsReloadDue()))
            {
                await ReloadAsync(cancellationToken).ConfigureAwait(false);
            }

            return _snapshot!;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Must be called while holding the gate.</summary>
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await EnsureSubscribedAsync(cancellationToken).ConfigureAwait(false);

        // Read the version before the data. A change that lands during the load
        // raises the version again, and the next read reloads once more.
        long version;
        try
        {
            version = await _versionStore.GetVersionAsync(_cacheName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Version store is unavailable; cache {CacheName} is loaded without a version.", _cacheName);
            version = UnknownVersion;
            Interlocked.Exchange(ref _latestKnownVersion, UnknownVersion);
        }

        ObserveVersion(version);
        ScheduleNextVersionCheck();

        IReadOnlyDictionary<TKey, TValue> items;
        try
        {
            items = await _dataSource.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (_snapshot is not null && !cancellationToken.IsCancellationRequested)
        {
            // Fail-safe: keep the previous data set. It stays stale, so a later read retries.
            ScheduleNextReloadAttempt();
            _logger.LogError(
                ex,
                "Reload of cache {CacheName} failed; serving the previous data set (version {Version}) and retrying in {RetryInterval}.",
                _cacheName,
                _snapshot.Version,
                _options.ReloadRetryInterval);
            return;
        }

        _snapshot = new Snapshot(new ConcurrentDictionary<TKey, TValue>(items), version);

        _logger.LogDebug("Cache {CacheName} loaded {Count} items at version {Version}.", _cacheName, items.Count, version);
    }

    /// <summary>Must be called while holding the gate.</summary>
    private async Task CheckVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EnsureSubscribedAsync(cancellationToken).ConfigureAwait(false);
            var version = await _versionStore.GetVersionAsync(_cacheName, cancellationToken).ConfigureAwait(false);

            if (version < _snapshot!.Version)
            {
                // The version went down: the Redis key was flushed or evicted. Reload to be safe.
                Interlocked.Exchange(ref _latestKnownVersion, version);
            }
            else
            {
                ObserveVersion(version);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Version check failed for cache {CacheName}; serving local data.", _cacheName);
        }
        finally
        {
            ScheduleNextVersionCheck();
        }
    }

    /// <summary>Must be called while holding the gate.</summary>
    private async Task EnsureSubscribedAsync(CancellationToken cancellationToken)
    {
        if (!_options.UsePubSub || _subscription is not null)
        {
            return;
        }

        try
        {
            _subscription = await _versionStore
                .SubscribeAsync(_cacheName, ObserveVersion, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Subscription failed for cache {CacheName}; relying on version checks.", _cacheName);
        }
    }

    /// <summary>Raises the latest known version; never lowers it. Safe to call from any thread.</summary>
    private void ObserveVersion(long version)
    {
        var current = Volatile.Read(ref _latestKnownVersion);
        while (version > current)
        {
            var previous = Interlocked.CompareExchange(ref _latestKnownVersion, version, current);
            if (previous == current)
            {
                return;
            }

            current = previous;
        }
    }

    private bool IsStale(Snapshot snapshot) => snapshot.Version != Volatile.Read(ref _latestKnownVersion);

    private bool IsVersionCheckDue() =>
        _timeProvider.GetUtcNow().UtcTicks >= Volatile.Read(ref _nextVersionCheckTicks);

    private void ScheduleNextVersionCheck() =>
        Volatile.Write(ref _nextVersionCheckTicks, (_timeProvider.GetUtcNow() + _options.VersionCheckInterval).UtcTicks);

    private bool IsReloadDue() =>
        _timeProvider.GetUtcNow().UtcTicks >= Volatile.Read(ref _nextReloadAttemptTicks);

    private void ScheduleNextReloadAttempt() =>
        Volatile.Write(ref _nextReloadAttemptTicks, (_timeProvider.GetUtcNow() + _options.ReloadRetryInterval).UtcTicks);

    private sealed record Snapshot(ConcurrentDictionary<TKey, TValue> Items, long Version);
}
