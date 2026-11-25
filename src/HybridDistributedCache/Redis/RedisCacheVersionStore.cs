using StackExchange.Redis;

namespace HybridDistributedCache.Redis;

/// <summary>
/// Keeps one integer per cache in Redis and announces every increment on a Pub/Sub channel.
/// Only the version number travels over the network; the cached data is never serialized.
/// </summary>
public sealed class RedisCacheVersionStore : ICacheVersionStore
{
    // INCR and PUBLISH in one round trip, so a successful increment is always announced.
    private const string IncrementAndPublishScript = """
        local version = redis.call('INCR', KEYS[1])
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

    public async Task<long> GetVersionAsync(string cacheName, CancellationToken cancellationToken = default)
    {
        var value = await _redis.GetDatabase()
            .StringGetAsync(VersionKey(cacheName))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        return value.TryParse(out long version) ? version : 0;
    }

    public async Task<long> IncrementVersionAsync(string cacheName, CancellationToken cancellationToken = default)
    {
        var result = await _redis.GetDatabase()
            .ScriptEvaluateAsync(
                IncrementAndPublishScript,
                [VersionKey(cacheName)],
                [ChannelName(cacheName)])
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        return (long)result;
    }

    public async Task<IAsyncDisposable> SubscribeAsync(
        string cacheName,
        Action<long> onVersionChanged,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onVersionChanged);

        var subscriber = _redis.GetSubscriber();
        var channel = RedisChannel.Literal(ChannelName(cacheName));

        void Handler(RedisChannel _, RedisValue message)
        {
            if (message.TryParse(out long version))
            {
                onVersionChanged(version);
            }
        }

        await subscriber.SubscribeAsync(channel, Handler).WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Subscription(subscriber, channel, Handler);
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
