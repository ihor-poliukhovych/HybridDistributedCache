using HybridDistributedCache.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace HybridDistributedCache.Tests;

public sealed class HybridCacheManagerTests : IAsyncLifetime
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);

    private readonly FakeDataSource _database = new();
    private readonly InMemoryCacheVersionStore _redis = new();
    private readonly FakeTimeProvider _time = new();
    private readonly List<HybridCacheManager<int, string>> _instances = [];

    public HybridCacheManagerTests()
    {
        _database.Rows[1] = "one";
        _database.Rows[2] = "two";
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task First_read_loads_the_data_set_once()
    {
        var cache = CreateInstance();

        Assert.Equal("one", await cache.GetAsync(1, Ct));
        Assert.Equal("two", await cache.GetAsync(2, Ct));
        Assert.Null(await cache.GetAsync(3, Ct));

        Assert.Equal(1, _database.LoadCount);
    }

    [Fact]
    public async Task Reads_between_version_checks_do_not_call_the_version_store()
    {
        var cache = CreateInstance();
        await cache.GetAsync(1, Ct);
        var callsAfterLoad = _redis.GetVersionCalls;

        for (var i = 0; i < 1_000; i++)
        {
            await cache.GetAsync(1, Ct);
        }

        Assert.Equal(callsAfterLoad, _redis.GetVersionCalls);
    }

    [Fact]
    public async Task Version_is_checked_again_after_the_interval()
    {
        var cache = CreateInstance();
        await cache.GetAsync(1, Ct);
        var callsAfterLoad = _redis.GetVersionCalls;

        _time.Advance(CheckInterval);
        await cache.GetAsync(1, Ct);
        await cache.GetAsync(1, Ct);

        Assert.Equal(callsAfterLoad + 1, _redis.GetVersionCalls);
    }

    [Fact]
    public async Task Write_on_one_instance_reaches_the_other_through_notification()
    {
        var instanceA = CreateInstance();
        var instanceB = CreateInstance();
        await instanceA.GetAsync(1, Ct);
        await instanceB.GetAsync(1, Ct);

        _database.Rows[1] = "uno";
        await instanceA.SetAsync(1, "uno", Ct);

        Assert.Equal("uno", await instanceA.GetAsync(1, Ct));
        Assert.Equal("uno", await instanceB.GetAsync(1, Ct));
    }

    [Fact]
    public async Task Remove_on_one_instance_reaches_the_other()
    {
        var instanceA = CreateInstance();
        var instanceB = CreateInstance();
        await instanceA.GetAsync(2, Ct);
        await instanceB.GetAsync(2, Ct);

        _database.Rows.TryRemove(2, out _);
        await instanceA.RemoveAsync(2, Ct);

        Assert.Null(await instanceA.GetAsync(2, Ct));
        Assert.Null(await instanceB.GetAsync(2, Ct));
    }

    [Fact]
    public async Task Own_write_does_not_trigger_a_reload()
    {
        var cache = CreateInstance();
        await cache.GetAsync(1, Ct);

        await cache.SetAsync(3, "three", Ct);

        Assert.Equal("three", await cache.GetAsync(3, Ct));
        Assert.Equal(1, _database.LoadCount);
    }

    [Fact]
    public async Task Lost_notification_is_caught_by_the_next_version_check()
    {
        var instanceA = CreateInstance();
        var instanceB = CreateInstance();
        await instanceA.GetAsync(1, Ct);
        await instanceB.GetAsync(1, Ct);

        _redis.DeliverNotifications = false;
        _database.Rows[1] = "uno";
        await instanceA.SetAsync(1, "uno", Ct);

        Assert.Equal("one", await instanceB.GetAsync(1, Ct));

        _time.Advance(CheckInterval);
        Assert.Equal("uno", await instanceB.GetAsync(1, Ct));
    }

    [Fact]
    public async Task Concurrent_write_from_another_instance_is_not_lost()
    {
        var instanceA = CreateInstance();
        var instanceB = CreateInstance();
        await instanceA.GetAsync(1, Ct);
        await instanceB.GetAsync(1, Ct);

        // B writes first, and A does not hear about it.
        _redis.DeliverNotifications = false;
        _database.Rows[2] = "dos";
        await instanceB.SetAsync(2, "dos", Ct);

        // A writes next. Its new version is two steps ahead, so A knows it missed a change.
        _database.Rows[1] = "uno";
        await instanceA.SetAsync(1, "uno", Ct);

        Assert.Equal("dos", await instanceA.GetAsync(2, Ct));
        Assert.Equal("uno", await instanceA.GetAsync(1, Ct));
    }

    [Fact]
    public async Task Many_concurrent_reads_trigger_a_single_load()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _database.LoadGate = gate.Task;
        var cache = CreateInstance();

        var reads = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(async () => await cache.GetAsync(1, Ct), Ct))
            .ToArray();

        await WaitUntilAsync(() => _database.LoadCount > 0);
        gate.SetResult();
        var results = await Task.WhenAll(reads);

        Assert.All(results, value => Assert.Equal("one", value));
        Assert.Equal(1, _database.LoadCount);
    }

    [Fact]
    public async Task Invalidate_makes_every_instance_reload()
    {
        var instanceA = CreateInstance();
        var instanceB = CreateInstance();
        await instanceA.GetAsync(1, Ct);
        await instanceB.GetAsync(1, Ct);

        _database.Rows[1] = "uno";
        _database.Rows[2] = "dos";
        await instanceA.InvalidateAsync(Ct);

        Assert.Equal(["dos", "uno"], (await instanceA.GetAllAsync(Ct)).Order());
        Assert.Equal(["dos", "uno"], (await instanceB.GetAllAsync(Ct)).Order());
    }

    [Fact]
    public async Task Reset_of_the_version_key_causes_a_reload()
    {
        var cache = CreateInstance();
        await cache.GetAsync(1, Ct);
        await cache.SetAsync(1, "one", Ct);
        await cache.SetAsync(1, "one", Ct);

        _redis.Reset();
        _database.Rows[1] = "uno";
        _time.Advance(CheckInterval);

        Assert.Equal("uno", await cache.GetAsync(1, Ct));
    }

    [Fact]
    public async Task Reset_of_the_version_key_is_noticed_when_the_counter_grows_back()
    {
        var cache = CreateInstance();
        await cache.GetAsync(1, Ct);
        await cache.SetAsync(1, "one", Ct);
        await cache.SetAsync(1, "one", Ct);

        // The key is flushed, and another writer brings the counter back to the same number
        // before this instance checks the version again.
        _redis.Reset();
        _redis.DeliverNotifications = false;
        _database.Rows[1] = "uno";
        await _redis.IncrementVersionAsync("test", Ct);
        await _redis.IncrementVersionAsync("test", Ct);

        _time.Advance(CheckInterval);

        Assert.Equal("uno", await cache.GetAsync(1, Ct));
    }

    [Fact]
    public async Task Unavailable_version_store_does_not_break_reads()
    {
        var cache = CreateInstance();
        await cache.GetAsync(1, Ct);

        _redis.IsAvailable = false;
        _time.Advance(CheckInterval);

        Assert.Equal("one", await cache.GetAsync(1, Ct));
        Assert.Equal(1, _database.LoadCount);
    }

    [Fact]
    public async Task Cache_loads_without_version_store_and_resyncs_when_it_returns()
    {
        _redis.IsAvailable = false;
        var cache = CreateInstance();

        Assert.Equal("one", await cache.GetAsync(1, Ct));

        _redis.IsAvailable = true;
        _database.Rows[1] = "uno";
        _time.Advance(CheckInterval);

        Assert.Equal("uno", await cache.GetAsync(1, Ct));
    }

    [Fact]
    public async Task Without_pub_sub_changes_arrive_after_the_interval()
    {
        var instanceA = CreateInstance(usePubSub: false);
        var instanceB = CreateInstance(usePubSub: false);
        await instanceA.GetAsync(1, Ct);
        await instanceB.GetAsync(1, Ct);

        _database.Rows[1] = "uno";
        await instanceA.SetAsync(1, "uno", Ct);
        Assert.Equal("one", await instanceB.GetAsync(1, Ct));

        _time.Advance(CheckInterval);
        Assert.Equal("uno", await instanceB.GetAsync(1, Ct));
    }

    [Fact]
    public async Task Failed_reload_serves_the_previous_data_and_pauses_before_retrying()
    {
        var instanceA = CreateInstance();
        var instanceB = CreateInstance();
        await instanceA.GetAsync(1, Ct);
        await instanceB.GetAsync(1, Ct);

        _database.Rows[1] = "uno";
        await instanceA.SetAsync(1, "uno", Ct);
        _database.IsAvailable = false;

        // B knows its copy is stale, but the reload fails: it keeps serving the old copy.
        Assert.Equal("one", await instanceB.GetAsync(1, Ct));
        Assert.Equal("one", await instanceB.GetAsync(1, Ct));
        Assert.Equal(3, _database.LoadCount);

        // Within the pause, nobody calls the data source again.
        _time.Advance(RetryInterval - TimeSpan.FromSeconds(1));
        Assert.Equal("one", await instanceB.GetAsync(1, Ct));
        Assert.Equal(3, _database.LoadCount);

        _database.IsAvailable = true;
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("uno", await instanceB.GetAsync(1, Ct));
        Assert.Equal(4, _database.LoadCount);
    }

    [Fact]
    public async Task Failed_first_load_reaches_the_caller()
    {
        _database.IsAvailable = false;
        var cache = CreateInstance();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await cache.GetAsync(1, Ct));

        _database.IsAvailable = true;
        Assert.Equal("one", await cache.GetAsync(1, Ct));
    }

    [Fact]
    public void Non_positive_retry_interval_is_rejected()
    {
        var options = new HybridCacheOptions { ReloadRetryInterval = TimeSpan.Zero };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HybridCacheManager<int, string>(_database, _redis, options, _time));
    }

    [Fact]
    public void Non_positive_interval_is_rejected()
    {
        var options = new HybridCacheOptions { VersionCheckInterval = TimeSpan.Zero };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HybridCacheManager<int, string>(_database, _redis, options, _time));
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var instance in _instances)
        {
            await instance.DisposeAsync();
        }
    }

    private HybridCacheManager<int, string> CreateInstance(bool usePubSub = true)
    {
        var options = new HybridCacheOptions
        {
            CacheName = "test",
            VersionCheckInterval = CheckInterval,
            UsePubSub = usePubSub,
            ReloadRetryInterval = RetryInterval,
        };

        var instance = new HybridCacheManager<int, string>(_database, _redis, options, _time);
        _instances.Add(instance);
        return instance;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
