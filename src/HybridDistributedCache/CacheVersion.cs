namespace HybridDistributedCache;

/// <summary>
/// The shared version of a cache: an epoch and a counter.
/// </summary>
/// <remarks>
/// The epoch is created together with the version and changes whenever the version is lost and
/// created again, for example after a Redis flush or eviction. Comparing the pair, and not only
/// the counter, makes such a reset visible even when the counter later grows back to the number
/// an instance has already seen.
/// </remarks>
/// <param name="Epoch">Identifies one life of the version; a new value after every reset.</param>
/// <param name="Counter">Number of changes recorded within the epoch.</param>
public sealed record CacheVersion(string Epoch, long Counter)
{
    /// <summary>
    /// True when this version is a later state than <paramref name="other"/>: another epoch,
    /// or a higher counter within the same epoch.
    /// </summary>
    public bool Supersedes(CacheVersion other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Epoch != other.Epoch || Counter > other.Counter;
    }

    /// <summary>True when this version is exactly one change after <paramref name="other"/>.</summary>
    public bool Follows(CacheVersion other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Epoch == other.Epoch && Counter == other.Counter + 1;
    }

    public override string ToString() => $"{Epoch}:{Counter}";
}
