using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using MotorDecisao.Application.PublishedFlows;

namespace MotorDecisao.Infrastructure.PublishedFlows;

/// <summary>
/// In-memory implementation of <see cref="IPublishedFlowCache"/>. On a cache hit
/// it serves the published snapshot straight from RAM, so a decision never reads
/// rules/formulas from the database. Correctness across multiple EKS replicas is
/// kept without a distributed cache by periodically re-checking a lightweight
/// stamp (published version id + publish time): if another replica published a
/// new version, the stamp changes and this replica reloads on its next access.
///
/// The cache is a singleton; because the loader depends on a scoped DbContext, DB
/// work is done inside a fresh DI scope per operation. A per-flow lock prevents a
/// cache stampede when several proposals for the same flow arrive at once.
/// </summary>
public sealed class InMemoryPublishedFlowCache : IPublishedFlowCache
{
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PublishedFlowCacheOptions _options;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    // Tracks which flow ids currently have a cache entry, so InvalidateAll can
    // drop them all (IMemoryCache has no built-in "remove everything").
    private readonly ConcurrentDictionary<Guid, byte> _cachedFlowIds = new();

    public InMemoryPublishedFlowCache(
        IMemoryCache cache,
        IServiceScopeFactory scopeFactory,
        PublishedFlowCacheOptions options)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
        _options = options;
    }

    public async Task<PublishedFlowSnapshot?> GetAsync(Guid flowId, CancellationToken cancellationToken = default)
    {
        // Fast path: a fresh-enough cached entry is served without any DB access.
        if (_cache.TryGetValue(CacheKey(flowId), out CacheEntry? entry)
            && entry is not null
            && !NeedsStampRecheck(entry))
        {
            return entry.Snapshot;
        }

        var gate = _locks.GetOrAdd(flowId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Re-check inside the lock: another caller may have just refreshed it.
            if (_cache.TryGetValue(CacheKey(flowId), out entry) && entry is not null)
            {
                if (!NeedsStampRecheck(entry))
                {
                    return entry.Snapshot;
                }

                // Time to verify the cached snapshot is still the published one.
                var currentStamp = await WithLoaderAsync(
                    (loader, ct) => loader.GetStampAsync(flowId, ct), cancellationToken);

                if (currentStamp is null)
                {
                    // Flow was unpublished. Drop the entry.
                    _cache.Remove(CacheKey(flowId));
                    return null;
                }

                if (currentStamp.Value == entry.Stamp)
                {
                    // Unchanged: keep the snapshot, just reset the recheck clock.
                    entry.LastStampCheckUtc = DateTime.UtcNow;
                    return entry.Snapshot;
                }

                // Changed elsewhere: fall through to reload.
            }

            var loaded = await WithLoaderAsync(
                (loader, ct) => loader.LoadAsync(flowId, ct), cancellationToken);

            if (loaded is null)
            {
                _cache.Remove(CacheKey(flowId));
                return null;
            }

            Store(flowId, loaded);
            return loaded;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Invalidate(Guid flowId)
    {
        _cache.Remove(CacheKey(flowId));
        _cachedFlowIds.TryRemove(flowId, out _);
    }

    public void InvalidateAll()
    {
        foreach (var flowId in _cachedFlowIds.Keys)
        {
            _cache.Remove(CacheKey(flowId));
        }
        _cachedFlowIds.Clear();
    }

    private void Store(Guid flowId, PublishedFlowSnapshot snapshot)
    {
        var entry = new CacheEntry
        {
            Snapshot = snapshot,
            Stamp = new PublishedFlowStamp(snapshot.FlowVersionId, snapshot.PublishedAt),
            LastStampCheckUtc = DateTime.UtcNow
        };

        _cache.Set(CacheKey(flowId), entry, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.AbsoluteTtl
        });
        _cachedFlowIds[flowId] = 1;
    }

    private bool NeedsStampRecheck(CacheEntry entry)
        => DateTime.UtcNow - entry.LastStampCheckUtc >= _options.StampRecheckInterval;

    /// <summary>Runs a loader operation inside a fresh DI scope.</summary>
    private async Task<T> WithLoaderAsync<T>(
        Func<IPublishedFlowLoader, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<IPublishedFlowLoader>();
        return await operation(loader, cancellationToken);
    }

    private static string CacheKey(Guid flowId) => $"published-flow:{flowId}";

    /// <summary>Mutable holder so the recheck timestamp can be updated in place.</summary>
    private sealed class CacheEntry
    {
        public required PublishedFlowSnapshot Snapshot { get; init; }
        public required PublishedFlowStamp Stamp { get; init; }
        public DateTime LastStampCheckUtc { get; set; }
    }
}
