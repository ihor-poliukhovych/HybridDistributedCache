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
    public async Task Increment_returns_the_new_version_and_get_reads_it()
    {
        await using var redis = await ConnectAsync();
        var store = new RedisCacheVersionStore(redis, _keyPrefix);

        Assert.Equal(0, await store.GetVersionAsync("products", Ct));
        Assert.Equal(1, await store.IncrementVersionAsync("products", Ct));
        Assert.Equal(2, await store.IncrementVersionAsync("products", Ct));
        Assert.Equal(2, await store.GetVersionAsync("products", Ct));
    }

    [Fact]
    public async Task Increment_on_one_connection_notifies_another()
    {
        await using var publisherConnection = await ConnectAsync();
        await using var subscriberConnection = await ConnectAsync();
        var publisher = new RedisCacheVersionStore(publisherConnection, _keyPrefix);
        var subscriber = new RedisCacheVersionStore(subscriberConnection, _keyPrefix);

        var received = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await subscriber.SubscribeAsync("products", v => received.TrySetResult(v), Ct);

        await publisher.IncrementVersionAsync("products", Ct);

        Assert.Equal(1, await received.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct));
    }

    private static async Task<ConnectionMultiplexer> ConnectAsync()
    {
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(ConnectionString),
            $"Set {ConnectionVariable} to run the Redis integration tests.");

        return await ConnectionMultiplexer.ConnectAsync(ConnectionString!);
    }
}
