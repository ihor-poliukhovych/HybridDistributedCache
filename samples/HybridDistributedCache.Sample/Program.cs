using HybridDistributedCache;
using HybridDistributedCache.Sample.Products;

var builder = WebApplication.CreateBuilder(args);

var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Set ConnectionStrings:Redis in configuration.");

var dataFilePath = Path.Combine(
    builder.Environment.ContentRootPath,
    builder.Configuration["Sample:DataFilePath"] ?? "App_Data/products.json");

builder.Services.AddRedisCacheVersionStore(redisConnectionString);
builder.Services.AddSingleton(new JsonFileProductStore(dataFilePath));
builder.Services.AddSingleton<ICacheDataSource<int, Product>>(sp => sp.GetRequiredService<JsonFileProductStore>());
builder.Services.AddHybridCacheManager<int, Product>(options =>
    builder.Configuration.GetSection("HybridCache").Bind(options));
builder.Services.AddSingleton<ProductService>();

var app = builder.Build();

var products = app.MapGroup("/products");

products.MapGet("/", (ProductService service, CancellationToken ct) => service.GetAllAsync(ct));

products.MapGet("/{id:int}", async (int id, ProductService service, CancellationToken ct) =>
    await service.GetAsync(id, ct) is { } product ? Results.Ok(product) : Results.NotFound());

products.MapPut("/{id:int}", async (int id, ProductInput input, ProductService service, CancellationToken ct) =>
    Results.Ok(await service.UpsertAsync(id, input, ct)));

products.MapDelete("/{id:int}", async (int id, ProductService service, CancellationToken ct) =>
    await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

app.Run();
