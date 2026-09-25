using Microsoft.EntityFrameworkCore;
using MotorDecisao.Application.PublishedFlows;
using MotorDecisao.Domain.Enums;
using MotorDecisao.Infrastructure.Persistence;

namespace MotorDecisao.Infrastructure.PublishedFlows;

/// <summary>
/// Loads published-flow data from PostgreSQL via EF Core. This is the source of
/// truth behind the in-memory cache. Queries are read-only and tracking is
/// disabled since the results become immutable snapshots.
/// </summary>
public sealed class EfPublishedFlowLoader : IPublishedFlowLoader
{
    private readonly MotorDecisaoDbContext _db;

    public EfPublishedFlowLoader(MotorDecisaoDbContext db)
    {
        _db = db;
    }

    public async Task<PublishedFlowStamp?> GetStampAsync(Guid flowId, CancellationToken cancellationToken = default)
    {
        // Single, cheap row: the published version's id + publish time.
        var stamp = await _db.FlowVersions
            .AsNoTracking()
            .Where(v => v.DecisionFlowId == flowId && v.Status == FlowVersionStatus.Published)
            .Select(v => new { v.Id, v.PublishedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (stamp is null || stamp.PublishedAt is null)
        {
            return null;
        }

        return new PublishedFlowStamp(stamp.Id, stamp.PublishedAt.Value);
    }

    public async Task<PublishedFlowSnapshot?> LoadAsync(Guid flowId, CancellationToken cancellationToken = default)
    {
        var version = await GraphQuery()
            .Where(v => v.DecisionFlowId == flowId && v.Status == FlowVersionStatus.Published)
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null || version.PublishedAt is null)
        {
            return null;
        }

        return await BuildSnapshotAsync(version, cancellationToken);
    }

    public async Task<PublishedFlowSnapshot?> LoadLatestByNameAsync(string policyName, CancellationToken cancellationToken = default)
    {
        // Versão MAIS RECENTE por nome (case-insensitive), qualquer status. Sem
        // filtro de Status: rascunhos valem. Ordena por VersionNumber desc para
        // pegar a última criada. O merge de globais e a montagem são os mesmos
        // do caminho publicado, para o runtime bater 1:1.
        var name = (policyName ?? string.Empty).Trim();
        var version = await GraphQuery()
            .Where(v => v.DecisionFlow != null && v.DecisionFlow.Name.ToLower() == name.ToLower())
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

        return version is null ? null : await BuildSnapshotAsync(version, cancellationToken);
    }

    public async Task<PublishedFlowSnapshot?> LoadByVersionAsync(
        Guid flowId, Guid versionId, CancellationToken cancellationToken = default)
    {
        // Versão específica por id, qualquer status (rascunho incluso). Mesma
        // montagem do caminho publicado (tabelas + globais), sem cache.
        var version = await GraphQuery()
            .Where(v => v.Id == versionId && v.DecisionFlowId == flowId)
            .FirstOrDefaultAsync(cancellationToken);

        return version is null ? null : await BuildSnapshotAsync(version, cancellationToken);
    }

    /// <summary>Consulta base do grafo de uma versão (nós/arestas/rulesets+regras).</summary>
    private IQueryable<Domain.Entities.FlowVersion> GraphQuery()
        => _db.FlowVersions
            .AsNoTracking()
            .Include(v => v.DecisionFlow)
            .Include(v => v.Nodes)
            .Include(v => v.Edges)
            .Include(v => v.Rulesets)
                .ThenInclude(r => r.Rules);

    /// <summary>
    /// Monta o <see cref="PublishedFlowSnapshot"/> a partir de uma versão já
    /// carregada com o grafo. Carrega as fórmulas locais da versão e faz o merge
    /// com as variáveis globais (locais têm precedência). Para versões sem
    /// PublishedAt (rascunho), usa o instante atual — o campo não é usado como
    /// stamp neste caminho, pois o resultado não é cacheado.
    /// </summary>
    private async Task<PublishedFlowSnapshot> BuildSnapshotAsync(
        Domain.Entities.FlowVersion version, CancellationToken cancellationToken)
    {
        // Formulas are scoped to the version; loaded separately to keep the graph
        // query focused.
        var localFormulas = await _db.Formulas
            .AsNoTracking()
            .Where(f => f.FlowVersionId == version.Id)
            .Select(f => new PublishedFormula(f.Key, f.Label, f.Expression))
            .ToListAsync(cancellationToken);

        // Global variables are shared across every flow; merge them in with the
        // flow's local variables taking precedence on key collisions.
        var globalFormulas = await _db.GlobalVariables
            .AsNoTracking()
            .Select(g => new PublishedFormula(g.Key, g.Label, g.Expression))
            .ToListAsync(cancellationToken);

        var formulas = VariableMerge.Merge(globalFormulas, localFormulas);

        // Tabelas de parâmetros: locais da versão + globais, com local vencendo.
        var localTables = await _db.ParameterTables
            .AsNoTracking()
            .Where(t => t.FlowVersionId == version.Id)
            .ToListAsync(cancellationToken);
        var globalTables = await _db.GlobalParameterTables
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var tables = ParameterTableMapper.Merge(
            globalTables.Select(ParameterTableMapper.ToPublished),
            localTables.Select(ParameterTableMapper.ToPublished));

        var nodes = version.Nodes
            .Select(n => new PublishedNode(n.NodeKey, n.Kind, n.Label, n.Config, n.RulesetId))
            .ToList();

        var edges = version.Edges
            .Select(e => new PublishedEdge(e.EdgeKey, e.SourceNodeKey, e.TargetNodeKey, e.SourceHandle, e.Label))
            .ToList();

        var rulesets = version.Rulesets
            .Select(rs => new PublishedRuleset(
                rs.Id,
                rs.Name,
                rs.ApprovalThreshold,
                rs.Rules
                    .OrderBy(r => r.Order)
                    .Select(r => new PublishedRule(
                        r.Id,
                        r.Order,
                        r.Name,
                        r.ConditionExpression,
                        r.Effect,
                        r.ScoreWeight,
                        r.ForcedOutcome,
                        r.Message))
                    .ToList()))
            .ToList();

        return new PublishedFlowSnapshot(
            FlowId: version.DecisionFlowId,
            FlowName: version.DecisionFlow?.Name ?? string.Empty,
            FlowVersionId: version.Id,
            VersionNumber: version.VersionNumber,
            PublishedAt: version.PublishedAt ?? DateTime.UtcNow,
            Nodes: nodes,
            Edges: edges,
            Rulesets: rulesets,
            Formulas: formulas)
        {
            Tables = tables,
        };
    }
}
