using HybridDistributedCache.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HybridDistributedCache;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RedisCacheVersionStore"/> and a shared Redis connection.
    /// The application starts even when Redis is down; the cache then serves local data
    /// and retries the version store on the next check.
    /// </summary>
    public static IServiceCollection AddRedisCacheVersionStore(
        this IServiceCollection services,
        string connectionString,
        string keyPrefix = "hdc")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        services.TryAddSingleton<ICacheVersionStore>(sp =>
            new RedisCacheVersionStore(sp.GetRequiredService<IConnectionMultiplexer>(), keyPrefix));

        return services;
    }

    /// <summary>
    /// Registers <see cref="IHybridCacheManager{TKey, TValue}"/> as a singleton.
    /// An <see cref="ICacheDataSource{TKey, TValue}"/> and an <see cref="ICacheVersionStore"/>
    /// must be registered as well.
    /// </summary>
    public static IServiceCollection AddHybridCacheManager<TKey, TValue>(
        this IServiceCollection services,
        Action<HybridCacheOptions>? configure = null)
        where TKey : notnull
        where TValue : class
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IHybridCacheManager<TKey, TValue>>(sp =>
        {
            var options = new HybridCacheOptions();
            configure?.Invoke(options);

            return new HybridCacheManager<TKey, TValue>(
                sp.GetRequiredService<ICacheDataSource<TKey, TValue>>(),
                sp.GetRequiredService<ICacheVersionStore>(),
                options,
                sp.GetRequiredService<TimeProvider>(),
                sp.GetService<ILogger<HybridCacheManager<TKey, TValue>>>());
        });

        return services;
    }
}
