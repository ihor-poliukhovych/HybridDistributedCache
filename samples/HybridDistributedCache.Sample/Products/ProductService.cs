namespace HybridDistributedCache.Sample.Products;

/// <summary>Reads go to the cache; writes go to the store first and then to the cache.</summary>
public sealed class ProductService(JsonFileProductStore store, IHybridCacheManager<int, Product> cache)
{
    public ValueTask<Product?> GetAsync(int id, CancellationToken cancellationToken) =>
        cache.GetAsync(id, cancellationToken);

    public async Task<IReadOnlyCollection<Product>> GetAllAsync(CancellationToken cancellationToken) =>
        (await cache.GetAllAsync(cancellationToken)).OrderBy(p => p.Id).ToArray();

    public async Task<Product> UpsertAsync(int id, ProductInput input, CancellationToken cancellationToken)
    {
        var product = new Product(id, input.Name, input.Price);
        await store.SaveAsync(product, cancellationToken);
        await cache.SetAsync(id, product, cancellationToken);
        return product;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        if (!await store.DeleteAsync(id, cancellationToken))
        {
            return false;
        }

        await cache.RemoveAsync(id, cancellationToken);
        return true;
    }
}
