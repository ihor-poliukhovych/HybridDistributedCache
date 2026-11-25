namespace HybridDistributedCache.Sample.Products;

/// <summary>Reference data that is read often and changed rarely: a good fit for this cache.</summary>
public sealed record Product(int Id, string Name, decimal Price);

public sealed record ProductInput(string Name, decimal Price);
