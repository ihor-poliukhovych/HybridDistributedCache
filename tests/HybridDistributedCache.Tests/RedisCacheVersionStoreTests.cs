using HybridDistributedCache.Redis;
using StackExchange.Redis;

namespace HybridDistributedCache.Tests;

/// <summary>
/// Runs against a real Redis when HDC_TEST_REDIS is set (for example "localhost:6379").
/// Without it, the tests are reported as skipped.
/// </summary>
public sealed class RedisCacheVersionStoreTests
{
    private const string ConnectionVariable = "HDC_TEST_REDIS";

    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable(ConnectionVariable);

    private readonly string _keyPrefix = $"hdc-test-{Guid.NewGuid():N}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Increment_raises_the_counter_within_one_epoch()
    {
        await using var redis = await ConnectAsync();
        var store = new RedisCacheVersionStore(redis, _keyPrefix);

        var initial = await store.GetVersionAsync("products", Ct);
        Assert.Equal(0, initial.Counter);
        Assert.False(string.IsNullOrEmpty(initial.Epoch));
        Assert.Equal(initial, await store.GetVersionAsync("products", Ct));

        Assert.Equal(new CacheVersion(initial.Epoch, 1), await store.IncrementVersionAsync("products", Ct));
        Assert.Equal(new CacheVersion(initial.Epoch, 2), await store.IncrementVersionAsync("products", Ct));
        Assert.Equal(new CacheVersion(initial.Epoch, 2), await store.GetVersionAsync("products", Ct));
    }

    [Fact]
    public async Task Deleted_key_comes_back_with_a_new_epoch()
    {
        await using var redis = await ConnectAsync();
        var store = new RedisCacheVersionStore(redis, _keyPrefix);

        await store.IncrementVersionAsync("products", Ct);
        var before = await store.IncrementVersionAsync("products", Ct);

        await redis.GetDatabase().KeyDeleteAsync($"{_keyPrefix}:products:version");
        await store.IncrementVersionAsync("products", Ct);
        var after = await store.IncrementVersionAsync("products", Ct);

        Assert.Equal(before.Counter, after.Counter);
        Assert.NotEqual(before.Epoch, after.Epoch);
        Assert.True(after.Supersedes(before));
    }

    [Fact]
    public async Task Increment_on_one_connection_notifies_another()
    {
        await using var publisherConnection = await ConnectAsync();
        await using var subscriberConnection = await ConnectAsync();
        var publisher = new RedisCacheVersionStore(publisherConnection, _keyPrefix);
        var subscriber = new RedisCacheVersionStore(subscriberConnection, _keyPrefix);

        var received = new TaskCompletionSource<CacheVersion>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await subscriber.SubscribeAsync("products", v => received.TrySetResult(v), Ct);

        var published = await publisher.IncrementVersionAsync("products", Ct);

        Assert.Equal(1, published.Counter);
        Assert.Equal(published, await received.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct));
    }

    private static async Task<ConnectionMultiplexer> ConnectAsync()
    {
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(ConnectionString),
            $"Set {ConnectionVariable} to run the Redis integration tests.");

        return await ConnectionMultiplexer.ConnectAsync(ConnectionString!);
    }
}
