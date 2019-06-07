using HybridDistributedCache.Cache;
using HybridDistributedCache.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using HybridDistributedCache.Configurations;
using HybridDistributedCache.Repositories;
using Microsoft.Extensions.Configuration;

namespace HybridDistributedCache
{
    public class Startup
    {
        private readonly IConfiguration _config;

        public Startup(IConfiguration config)
        {
            this._config = config;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();

            var cacheConfiguration = new CacheConfiguration();
            this._config.Bind(nameof(CacheConfiguration), cacheConfiguration);

            services.AddDistributedRedisCache(options =>
                {
                    options.Configuration = cacheConfiguration.RedisConnectionString;
                });

            services.Configure<CacheConfiguration>(this._config.GetSection(nameof(CacheConfiguration)));
            services.AddScoped<IMyComplexValueRepository, MyComplexValueRepository>();
            services.AddSingleton<IHybridCacheManager<int, MyComplexValue>, HybridCacheManager<int, MyComplexValue>>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseMvc();
        }
    }
}
