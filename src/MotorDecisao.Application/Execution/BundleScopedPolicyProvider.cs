using MotorDecisao.Application.PublishedFlows;

namespace MotorDecisao.Application.Execution;

/// <summary>
/// Resolvedor de subpolítica por nome RESTRITO a um bundle congelado. Durante uma
/// decisão que roda a partir de um <see cref="PublishedBundle"/>, as políticas
/// referenciadas (<c>$[Política;...]</c>) são resolvidas exclusivamente do retrato
/// congelado — nunca da versão "mais recente" do banco. Assim, editar/versionar
/// uma subpolítica depois não altera decisões da publicação vigente.
///
/// Compila cada snapshot do bundle sob demanda e cacheia por nome (imutável
/// durante a decisão). Usado por decisão (instância descartável), então não há
/// concorrência.
/// </summary>
public sealed class BundleScopedPolicyProvider : IPolicyByNameProvider
{
    private readonly IReadOnlyDictionary<string, PublishedFlowSnapshot> _byName;
    private readonly Dictionary<string, CompiledFlow?> _compiled =
        new(StringComparer.OrdinalIgnoreCase);

    public BundleScopedPolicyProvider(BundleContent bundle)
    {
        // Indexa os snapshots do bundle por nome de política (case-insensitive).
        var map = new Dictionary<string, PublishedFlowSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var snap in bundle.Snapshots.Values)
        {
            map[snap.FlowName] = snap;
        }
        _byName = map;
    }

    public Task<CompiledFlow?> GetByNameAsync(string policyName, CancellationToken cancellationToken = default)
    {
        if (_compiled.TryGetValue(policyName, out var cached))
        {
            return Task.FromResult(cached);
        }

        CompiledFlow? flow = null;
        if (_byName.TryGetValue(policyName, out var snapshot))
        {
            flow = CompiledFlow.Compile(snapshot);
        }
        _compiled[policyName] = flow;
        return Task.FromResult(flow);
    }
}
