using System.Collections.Concurrent;
using MotorDecisao.Application.Execution;
using MotorDecisao.Application.PublishedFlows;

namespace MotorDecisao.Infrastructure.PublishedFlows;

/// <summary>
/// Turns the cached published-flow snapshot into a <see cref="CompiledFlow"/>
/// (formulas pre-parsed) and caches that compiled form too, keyed by the
/// published version id. Because a published version is immutable, the compiled
/// artifact is safe to reuse until the version changes.
///
/// It relies on <see cref="IPublishedFlowCache"/> for the snapshot (which already
/// handles TTL + multi-replica stamp revalidation), and only re-compiles when the
/// underlying published version id changes.
/// </summary>
public sealed class CompiledFlowProvider : ICompiledFlowProvider
{
    private readonly IPublishedFlowCache _snapshots;

    // flowId -> (published version id, compiled flow). Recompiled when the
    // version id no longer matches what the snapshot cache returns.
    private readonly ConcurrentDictionary<Guid, (Guid VersionId, CompiledFlow Flow)> _compiled = new();

    public CompiledFlowProvider(IPublishedFlowCache snapshots)
    {
        _snapshots = snapshots;
    }

    public async Task<CompiledFlow?> GetAsync(Guid flowId, CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshots.GetAsync(flowId, cancellationToken);
        if (snapshot is null)
        {
            _compiled.TryRemove(flowId, out _);
            return null;
        }

        if (_compiled.TryGetValue(flowId, out var cached) && cached.VersionId == snapshot.FlowVersionId)
        {
            return cached.Flow;
        }

        // First use or the published version changed: compile and cache.
        var compiled = CompiledFlow.Compile(snapshot);
        _compiled[flowId] = (snapshot.FlowVersionId, compiled);
        return compiled;
    }

    public void Invalidate(Guid flowId)
    {
        _compiled.TryRemove(flowId, out _);
        _snapshots.Invalidate(flowId);
    }

    public void InvalidateAll()
    {
        _compiled.Clear();
        _snapshots.InvalidateAll();
    }
}
