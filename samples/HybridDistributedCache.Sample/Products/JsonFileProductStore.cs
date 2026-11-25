using System.Text.Json;

namespace HybridDistributedCache.Sample.Products;

/// <summary>
/// Stand-in for a database: a JSON file that all sample instances on one machine share.
/// It is the source of truth; the cache only keeps a copy of it in memory.
/// </summary>
public sealed class JsonFileProductStore : ICacheDataSource<int, Product>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly Product[] SeedData =
    [
        new(1, "Keyboard", 49.90m),
        new(2, "Mouse", 19.90m),
        new(3, "Monitor", 189.00m),
    ];

    private readonly string _filePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonFileProductStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<IReadOnlyDictionary<int, Product>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var products = await ReadAsync(cancellationToken);
        return products.ToDictionary(p => p.Id);
    }

    public async Task SaveAsync(Product product, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var products = (await ReadAsync(cancellationToken)).Where(p => p.Id != product.Id).Append(product);
            await WriteAsync(products.OrderBy(p => p.Id).ToArray(), cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var products = await ReadAsync(cancellationToken);
            var remaining = products.Where(p => p.Id != id).ToArray();
            if (remaining.Length == products.Count)
            {
                return false;
            }

            await WriteAsync(remaining, cancellationToken);
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<IReadOnlyCollection<Product>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return SeedData;
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<Product[]>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private async Task WriteAsync(IReadOnlyCollection<Product> products, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_filePath))!);

        // Write to a temporary file and swap it in, so a reader never sees half a file.
        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, products, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }
}
