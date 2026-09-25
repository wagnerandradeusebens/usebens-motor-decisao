using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using MotorDecisao.Application.PublishedFlows;
using MotorDecisao.Infrastructure.Configuration;
using StackExchange.Redis;

namespace MotorDecisao.Infrastructure.PublishedFlows;

/// <summary>
/// Redis-backed implementation of <see cref="IPublishedFlowCache"/>. It serves the
/// published snapshot from Redis so a decision doesn't read rules/formulas from
/// PostgreSQL on the hot path, and — unlike the in-memory cache — it is shared by
/// all replicas, so a publish (or a global-variable change that calls
/// <see cref="InvalidateAll"/>) is seen everywhere without a stamp dance.
///
/// The snapshot is stored as JSON. A lightweight stamp (published version id +
/// publish time) is re-checked periodically to pick up publishes; if Redis is
/// momentarily unreachable, calls degrade to a cache miss (reload from the DB)
/// rather than throwing.
/// </summary>
public sealed class RedisPublishedFlowCache : IPublishedFlowCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PublishedFlowCacheOptions _options;
    private readonly string _keyPrefix;

    // A Redis SET tracking which flow ids currently have an entry, so
    // InvalidateAll can delete them all without SCAN/KEYS.
    private readonly string _indexKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public RedisPublishedFlowCache(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        PublishedFlowCacheOptions options,
        RedisOptions redisOptions)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _options = options;
        _keyPrefix = redisOptions.KeyPrefix;
        _indexKey = $"{_keyPrefix}index";
    }

    public async Task<PublishedFlowSnapshot?> GetAsync(Guid flowId, CancellationToken cancellationToken = default)
    {
        var db = TryGetDatabase();
        if (db is null)
        {
            // Redis unreachable: fall back to a direct load so decisions still work.
            return await LoadAsync(flowId, cancellationToken);
        }

        try
        {
            var cachedJson = await db.StringGetAsync(CacheKey(flowId));
            if (cachedJson.HasValue)
            {
                var entry = Deserialize(cachedJson!);
                if (entry is not null && !NeedsStampRecheck(entry))
                {
                    return entry.Snapshot;
                }

                // Time to verify the cached snapshot is still the published one.
                var currentStamp = await WithLoaderAsync(
                    (loader, ct) => loader.GetStampAsync(flowId, ct), cancellationToken);

                if (currentStamp is null)
                {
                    await RemoveAsync(db, flowId);
                    return null;
                }

                if (entry is not null && currentStamp.Value == entry.Stamp)
                {
                    // Unchanged: refresh the recheck clock and keep serving it.
                    entry.LastStampCheckUtc = DateTime.UtcNow;
                    await StoreAsync(db, flowId, entry);
                    return entry.Snapshot;
                }
                // Changed elsewhere: fall through to reload.
            }

            var loaded = await LoadAsync(flowId, cancellationToken);
            if (loaded is null)
            {
                await RemoveAsync(db, flowId);
                return null;
            }

            await StoreAsync(db, flowId, new CacheEntry
            {
                Snapshot = loaded,
                Stamp = new PublishedFlowStamp(loaded.FlowVersionId, loaded.PublishedAt),
                LastStampCheckUtc = DateTime.UtcNow
            });
            return loaded;
        }
        catch (RedisException)
        {
            // Any Redis hiccup degrades to a direct DB load.
            return await LoadAsync(flowId, cancellationToken);
        }
    }

    public void Invalidate(Guid flowId)
    {
        var db = TryGetDatabase();
        if (db is null) return;
        try
        {
            db.KeyDelete(CacheKey(flowId));
            db.SetRemove(_indexKey, flowId.ToString());
        }
        catch (RedisException)
        {
            // Best-effort: a stale entry will be corrected by the next stamp check.
        }
    }

    public void InvalidateAll()
    {
        var db = TryGetDatabase();
        if (db is null) return;
        try
        {
            var ids = db.SetMembers(_indexKey);
            foreach (var id in ids)
            {
                db.KeyDelete($"{_keyPrefix}{id}");
            }
            db.KeyDelete(_indexKey);
        }
        catch (RedisException)
        {
            // Best-effort; entries will still expire via TTL / stamp recheck.
        }
    }

    private async Task<PublishedFlowSnapshot?> LoadAsync(Guid flowId, CancellationToken ct)
        => await WithLoaderAsync((loader, c) => loader.LoadAsync(flowId, c), ct);

    private async Task StoreAsync(IDatabase db, Guid flowId, CacheEntry entry)
    {
        var json = JsonSerializer.Serialize(entry, JsonOptions);
        await db.StringSetAsync(CacheKey(flowId), json, _options.AbsoluteTtl);
        await db.SetAddAsync(_indexKey, flowId.ToString());
    }

    private async Task RemoveAsync(IDatabase db, Guid flowId)
    {
        await db.KeyDeleteAsync(CacheKey(flowId));
        await db.SetRemoveAsync(_indexKey, flowId.ToString());
    }

    private IDatabase? TryGetDatabase()
    {
        try
        {
            // With abortConnect=false the multiplexer is always returned; individual
            // operations throw RedisException when the server is unreachable, which
            // callers catch and degrade to a direct DB load.
            return _redis.GetDatabase();
        }
        catch (RedisException)
        {
            return null;
        }
    }

    private bool NeedsStampRecheck(CacheEntry entry)
        => DateTime.UtcNow - entry.LastStampCheckUtc >= _options.StampRecheckInterval;

    private static CacheEntry? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CacheEntry>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Runs a loader operation inside a fresh DI scope (scoped DbContext).</summary>
    private async Task<T> WithLoaderAsync<T>(
        Func<IPublishedFlowLoader, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<IPublishedFlowLoader>();
        return await operation(loader, cancellationToken);
    }

    private string CacheKey(Guid flowId) => $"{_keyPrefix}{flowId}";

    /// <summary>What we persist per flow: the snapshot plus stamp metadata.</summary>
    private sealed class CacheEntry
    {
        public required PublishedFlowSnapshot Snapshot { get; init; }
        public required PublishedFlowStamp Stamp { get; init; }
        public DateTime LastStampCheckUtc { get; set; }
    }
}
