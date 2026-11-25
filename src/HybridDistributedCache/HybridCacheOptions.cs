namespace HybridDistributedCache;

public sealed class HybridCacheOptions
{
    /// <summary>
    /// Name of the cache in the version store. Instances that share data must use the same name.
    /// Defaults to the full name of the value type.
    /// </summary>
    public string? CacheName { get; set; }

    /// <summary>
    /// How often a read may ask the version store for the current version.
    /// This is the safety net for lost notifications and the upper bound of staleness
    /// when Pub/Sub is off. Reads between checks never leave the process.
    /// </summary>
    public TimeSpan VersionCheckInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Subscribe to change notifications so that other instances react immediately.</summary>
    public bool UsePubSub { get; set; } = true;

    internal void Validate()
    {
        if (VersionCheckInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(VersionCheckInterval), VersionCheckInterval, "The interval must be positive.");
        }
    }
}
