using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
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
public sealed class CompiledFlowProvider : ICompiledFlowProvider, IPolicyByNameProvider
{
    private readonly IPublishedFlowCache _snapshots;
    private readonly IServiceScopeFactory _scopeFactory;

    // flowId -> (published version id, compiled flow). Recompiled when the
    // version id no longer matches what the snapshot cache returns.
    private readonly ConcurrentDictionary<Guid, (Guid VersionId, CompiledFlow Flow)> _compiled = new();

    public CompiledFlowProvider(IPublishedFlowCache snapshots, IServiceScopeFactory scopeFactory)
    {
        _snapshots = snapshots;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Resolve uma política SECUNDÁRIA (referenciada por nome) pela sua versão
    /// MAIS RECENTE — qualquer status, inclusive rascunho. Só a política principal
    /// precisa estar publicada; as referenciadas valem sempre pela última versão.
    ///
    /// Diferente do caminho publicado (por id), este NÃO usa o cache: a versão de
    /// rascunho é mutável, então compilamos a cada resolução para sempre refletir
    /// o estado atual. O custo é baixo (compilação é rápida e há poucas
    /// referências por decisão) e evita servir um rascunho desatualizado.
    /// </summary>
    public async Task<CompiledFlow?> GetByNameAsync(string policyName, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<IPublishedFlowLoader>();

        var snapshot = await loader.LoadLatestByNameAsync(policyName, cancellationToken);
        if (snapshot is null)
        {
            return null; // não existe política com esse nome
        }

        return CompiledFlow.Compile(snapshot);
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
