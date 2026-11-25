using System.Collections.Concurrent;

namespace HybridDistributedCache.Tests.Fakes;

/// <summary>The shared "database" behind several cache instances.</summary>
internal sealed class FakeDataSource : ICacheDataSource<int, string>
{
    private int _loadCount;

    public ConcurrentDictionary<int, string> Rows { get; } = new();

    public int LoadCount => Volatile.Read(ref _loadCount);

    /// <summary>When set, every load waits for this task before it reads the rows.</summary>
    public Task? LoadGate { get; set; }

    public async Task<IReadOnlyDictionary<int, string>> LoadAllAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _loadCount);
        if (LoadGate is not null)
        {
            await LoadGate.WaitAsync(cancellationToken);
        }

        return new Dictionary<int, string>(Rows);
    }
}
