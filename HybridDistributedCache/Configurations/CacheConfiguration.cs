using System;

namespace HybridDistributedCache.Configurations
{
    public class CacheConfiguration
    {
        public string RedisConnectionString { get; set; }
        public TimeSpan SlidingExpiration { get; set; }
    }
}
