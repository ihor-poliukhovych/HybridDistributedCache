using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HybridDistributedCache.Cache
{
    public interface IHybridCacheManager<TKey, TValue> where TValue : class
    {
        Task<TValue> GetAsync(TKey key, CancellationToken token = default(CancellationToken));
        void Set(TKey key, TValue value);
        void Remove(TKey key);

        void ConfigureLoadData(Func<Task<IDictionary<TKey, TValue>>> loadDataFunc);
    }
}