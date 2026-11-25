using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using StackExchange.Redis;

namespace HybridDistributedCache.Redis;

/// <summary>
/// Keeps one small hash per cache in Redis (an epoch and a counter) and announces every
/// increment on a Pub/Sub channel. Only the version travels over the network; the cached data
/// is never serialized.
/// </summary>
/// <remarks>
/// The version is sent as <c>epoch:counter</c>. When the key is missing (never written, flushed
/// or evicted), the next read or increment creates it with a new epoch.
/// </remarks>
public sealed class RedisCacheVersionStore : ICacheVersionStore
{
    // Creates the epoch if the key is missing, then returns the current version.
    private const string GetScript = """
        redis.call('HSETNX', KEYS[1], 'epoch', ARGV[1])
        local version = redis.call('HMGET', KEYS[1], 'epoch', 'counter')
        return version[1] .. ':' .. (version[2] or '0')
        """;

    // HINCRBY and PUBLISH in one atomic script, so a successful increment is always announced.
    private const string IncrementAndPublishScript = """
        redis.call('HSETNX', KEYS[1], 'epoch', ARGV[2])
        local counter = redis.call('HINCRBY', KEYS[1], 'counter', 1)
        local version = redis.call('HGET', KEYS[1], 'epoch') .. ':' .. counter
        redis.call('PUBLISH', ARGV[1], version)
        return version
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly string _keyPrefix;

    public RedisCacheVersionStore(IConnectionMultiplexer redis, string keyPrefix = "hdc")
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

        _redis = redis;
        _keyPrefix = keyPrefix;
    }

    public async Task<CacheVersion> GetVersionAsync(string cacheName, CancellationToken cancellationToken = default)
    {
        var result = await _redis.GetDatabase()
            .ScriptEvaluateAsync(GetScript, [VersionKey(cacheName)], [NewEpoch()])
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        return Parse(result);
    }

    public async Task<CacheVersion> IncrementVersionAsync(string cacheName, CancellationToken cancellationToken = default)
    {
        var result = await _redis.GetDatabase()
            .ScriptEvaluateAsync(
                IncrementAndPublishScript,
                [VersionKey(cacheName)],
                [ChannelName(cacheName), NewEpoch()])
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        return Parse(result);
    }

    public async Task<IAsyncDisposable> SubscribeAsync(
        string cacheName,
        Action<CacheVersion> onVersionChanged,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onVersionChanged);

        var subscriber = _redis.GetSubscriber();
        var channel = RedisChannel.Literal(ChannelName(cacheName));

        void Handler(RedisChannel _, RedisValue message)
        {
            if (TryParse(message, out var version))
            {
                onVersionChanged(version);
            }
        }

        await subscriber.SubscribeAsync(channel, Handler).WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Subscription(subscriber, channel, Handler);
    }

    private static string NewEpoch() => Guid.NewGuid().ToString("N");

    private static CacheVersion Parse(RedisResult result) =>
        TryParse((string?)result, out var version)
            ? version
            : throw new InvalidOperationException($"Unexpected cache version format: '{result}'.");

    private static bool TryParse(string? value, [NotNullWhen(true)] out CacheVersion? version)
    {
        version = null;
        var separator = value?.LastIndexOf(':') ?? -1;
        if (value is null
            || separator <= 0
            || !long.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var counter))
        {
            return false;
        }

        version = new CacheVersion(value[..separator], counter);
        return true;
    }

    private string VersionKey(string cacheName) => $"{_keyPrefix}:{cacheName}:version";

    private string ChannelName(string cacheName) => $"{_keyPrefix}:{cacheName}:changed";

    private sealed class Subscription(
        ISubscriber subscriber,
        RedisChannel channel,
        Action<RedisChannel, RedisValue> handler) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await subscriber.UnsubscribeAsync(channel, handler).ConfigureAwait(false);
    }
}
