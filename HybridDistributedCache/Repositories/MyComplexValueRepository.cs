using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HybridDistributedCache.Cache;
using HybridDistributedCache.Models;

namespace HybridDistributedCache.Repositories
{
    public class MyComplexValueRepository : IMyComplexValueRepository
    {
        private readonly IEnumerable<MyComplexValue> _fakeData = new[]
            {
                new MyComplexValue(1, "test1", new[] { new MyValue(Guid.NewGuid(), "value1") }),
                new MyComplexValue(2, "test2", new[] { new MyValue(Guid.NewGuid(), "value2") }),
                new MyComplexValue(3, "test3", new[] { new MyValue(Guid.NewGuid(), "value3") })
            };

        private readonly IHybridCacheManager<int, MyComplexValue> _cacheManager;

        public MyComplexValueRepository(IHybridCacheManager<int, MyComplexValue> cacheManager)
        {
            this._cacheManager = cacheManager;
            this._cacheManager.ConfigureLoadData(this.LoadAsync);
        }

        /// <inheritdoc />
        public Task<MyComplexValue> GetAsync(int id)
        {
            return this._cacheManager.GetAsync(id);
        }

        /// <inheritdoc />
        public MyComplexValue Post(MyComplexValue value)
        {
            this._cacheManager.Set(value.Id, value);
            return value;
        }

        /// <inheritdoc />
        public MyComplexValue Put(int id, MyComplexValue value)
        {
            this._cacheManager.Set(id, value);
            return value;
        }

        /// <inheritdoc />
        public void Remove(int id)
        {
            this._cacheManager.Remove(id);
        }

        private Task<IDictionary<int, MyComplexValue>> LoadAsync()
        {
            IDictionary<int, MyComplexValue> result = this._fakeData.ToDictionary(x => x.Id, x => x);
            return Task.FromResult(result);
        }
    }
}
