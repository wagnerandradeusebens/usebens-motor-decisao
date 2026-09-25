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
        var version = await _db.FlowVersions
            .AsNoTracking()
            .Include(v => v.DecisionFlow)
            .Include(v => v.Nodes)
            .Include(v => v.Edges)
            .Include(v => v.Rulesets)
                .ThenInclude(r => r.Rules)
            .Where(v => v.DecisionFlowId == flowId && v.Status == FlowVersionStatus.Published)
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null || version.PublishedAt is null)
        {
            return null;
        }

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
            FlowId: flowId,
            FlowName: version.DecisionFlow?.Name ?? string.Empty,
            FlowVersionId: version.Id,
            VersionNumber: version.VersionNumber,
            PublishedAt: version.PublishedAt.Value,
            Nodes: nodes,
            Edges: edges,
            Rulesets: rulesets,
            Formulas: formulas);
    }
}
