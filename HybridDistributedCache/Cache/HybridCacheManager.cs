using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HybridDistributedCache.Configurations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace HybridDistributedCache.Cache
{
    public class HybridCacheManager<TKey, TValue> : IHybridCacheManager<TKey, TValue> where TValue : class
    {
        private readonly IDistributedCache _distributedCache;
        private readonly IOptions<CacheConfiguration> _cacheConfiguration;

        private Func<Task<IDictionary<TKey, TValue>>> _loadDataAsync;
        private readonly string _syncPointKey = $"SyncPoint.{typeof(TValue).FullName}";
        private ConcurrentDictionary<TKey, TValue> _inMemoryCache = new ConcurrentDictionary<TKey, TValue>();

        public HybridCacheManager(IOptions<CacheConfiguration> cacheConfiguration, IDistributedCache distributedCache)
        {
            this._cacheConfiguration = cacheConfiguration;
            this._distributedCache = distributedCache;
        }

        public void ConfigureLoadData(Func<Task<IDictionary<TKey, TValue>>> loadDataFunc)
        {
            this._loadDataAsync = loadDataFunc;
        }

        public virtual async Task<TValue> GetAsync(TKey key, CancellationToken token = default(CancellationToken))
        {
            if (await this._distributedCache.GetAsync(this._syncPointKey, token) == null)
            {
                await this.ReloadCache();
            }

            return this._inMemoryCache.TryGetValue(key, out var value) ? value : null;
        }

        public virtual void Set(TKey key, TValue value)
        {
            this._inMemoryCache.AddOrUpdate(key, value, (k, v) => value);
        }

        public virtual void Remove(TKey key)
        {
            this._inMemoryCache.TryRemove(key, out _);
        }

        protected virtual async Task ReloadCache()
        {
            var data = await this._loadDataAsync();
            this._inMemoryCache = new ConcurrentDictionary<TKey, TValue>(data);

            var options = new DistributedCacheEntryOptions { SlidingExpiration = this._cacheConfiguration.Value.SlidingExpiration };
            await this._distributedCache.SetStringAsync(this._syncPointKey, DateTime.UtcNow.ToString("G"), options);
        }
    }
}