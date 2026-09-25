using Microsoft.EntityFrameworkCore;
using MotorDecisao.Application.Common;
using MotorDecisao.Application.Execution;
using MotorDecisao.Application.Flows;
using MotorDecisao.Application.PublishedFlows;
using MotorDecisao.Domain.Entities;
using MotorDecisao.Domain.Enums;
using MotorDecisao.Infrastructure.Persistence;

namespace MotorDecisao.Infrastructure.Flows;

/// <summary>
/// EF Core implementation of the flow authoring/publishing use cases. All graph
/// edits target Draft versions; publishing validates the graph by compiling it
/// (reusing the execution engine's <see cref="CompiledFlow.Compile"/>) and swaps
/// the published version atomically, then invalidates the compiled-flow cache.
/// </summary>
public sealed class FlowManagementService : IFlowManagementService
{
    private readonly MotorDecisaoDbContext _db;
    private readonly ICompiledFlowProvider _compiledFlows;

    public FlowManagementService(MotorDecisaoDbContext db, ICompiledFlowProvider compiledFlows)
    {
        _db = db;
        _compiledFlows = compiledFlows;
    }

    public async Task<OperationResult<FlowSummary>> CreateFlowAsync(CreateFlowInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return OperationResult<FlowSummary>.Invalid("O nome do fluxo é obrigatório.");
        }

        var exists = await _db.DecisionFlows.AnyAsync(f => f.Name == input.Name, ct);
        if (exists)
        {
            return OperationResult<FlowSummary>.Conflict($"Já existe um fluxo com o nome '{input.Name}'.");
        }

        var flow = new DecisionFlow
        {
            Name = input.Name,
            Description = input.Description,
            IsActive = true
        };
        flow.Versions.Add(new FlowVersion
        {
            VersionNumber = 1,
            Status = FlowVersionStatus.Draft
        });

        _db.DecisionFlows.Add(flow);
        await _db.SaveChangesAsync(ct);

        return OperationResult<FlowSummary>.Ok(ToSummary(flow));
    }

    public async Task<IReadOnlyList<FlowSummary>> ListFlowsAsync(CancellationToken ct = default)
    {
        var flows = await _db.DecisionFlows
            .AsNoTracking()
            .Include(f => f.Versions)
            .OrderBy(f => f.Name)
            .ToListAsync(ct);

        return flows.Select(ToSummary).ToList();
    }

    public async Task<OperationResult<FlowSummary>> GetFlowAsync(Guid flowId, CancellationToken ct = default)
    {
        var flow = await _db.DecisionFlows
            .AsNoTracking()
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Id == flowId, ct);

        return flow is null
            ? OperationResult<FlowSummary>.NotFound($"Fluxo {flowId} não encontrado.")
            : OperationResult<FlowSummary>.Ok(ToSummary(flow));
    }

    public async Task<OperationResult<VersionGraph>> GetVersionGraphAsync(Guid flowId, Guid versionId, CancellationToken ct = default)
    {
        var version = await LoadVersionWithGraph(flowId, versionId, tracking: false, ct);
        if (version is null)
        {
            return OperationResult<VersionGraph>.NotFound($"Versão {versionId} não encontrada no fluxo {flowId}.");
        }
        var formulas = await LoadFormulas(versionId, ct);
        return OperationResult<VersionGraph>.Ok(ToGraph(version, formulas));
    }

    public async Task<OperationResult<VersionGraph>> SaveVersionGraphAsync(Guid flowId, Guid versionId, VersionGraph graph, CancellationToken ct = default)
    {
        var version = await LoadVersionWithGraph(flowId, versionId, tracking: true, ct);
        if (version is null)
        {
            return OperationResult<VersionGraph>.NotFound($"Versão {versionId} não encontrada no fluxo {flowId}.");
        }
        if (version.Status != FlowVersionStatus.Draft)
        {
            return OperationResult<VersionGraph>.Conflict(
                "Apenas versões em rascunho (Draft) podem ser editadas.");
        }

        // Replace the whole graph: clear existing children, then re-add.
        _db.FlowNodes.RemoveRange(version.Nodes);
        _db.FlowEdges.RemoveRange(version.Edges);
        foreach (var rs in version.Rulesets)
        {
            _db.Rules.RemoveRange(rs.Rules);
        }
        _db.Rulesets.RemoveRange(version.Rulesets);
        var oldFormulas = await _db.Formulas.Where(f => f.FlowVersionId == versionId).ToListAsync(ct);
        _db.Formulas.RemoveRange(oldFormulas);
        var oldInputFields = await _db.InputFields.Where(f => f.FlowVersionId == versionId).ToListAsync(ct);
        _db.InputFields.RemoveRange(oldInputFields);

        // Rulesets first, to map client RulesetKey -> real id for node references.
        var rulesetIdByKey = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var rs in graph.Rulesets)
        {
            var entity = new Ruleset
            {
                FlowVersionId = versionId,
                Name = rs.Name,
                Description = rs.Description,
                ApprovalThreshold = rs.ApprovalThreshold
            };
            foreach (var r in rs.Rules)
            {
                entity.Rules.Add(new Rule
                {
                    Order = r.Order,
                    Name = r.Name,
                    ConditionExpression = r.ConditionExpression,
                    Effect = r.Effect,
                    ScoreWeight = r.ScoreWeight,
                    ForcedOutcome = r.ForcedOutcome,
                    Message = r.Message,
                    IsEnabled = r.IsEnabled
                });
            }
            _db.Rulesets.Add(entity);
            rulesetIdByKey[rs.RulesetKey] = entity.Id;
        }

        foreach (var n in graph.Nodes)
        {
            Guid? rulesetId = null;
            if (n.RulesetKey is not null && rulesetIdByKey.TryGetValue(n.RulesetKey, out var rid))
            {
                rulesetId = rid;
            }

            _db.FlowNodes.Add(new FlowNode
            {
                FlowVersionId = versionId,
                NodeKey = n.NodeKey,
                Kind = n.Kind,
                Label = n.Label,
                PositionX = n.PositionX,
                PositionY = n.PositionY,
                Config = string.IsNullOrWhiteSpace(n.Config) ? "{}" : n.Config,
                RulesetId = rulesetId
            });
        }

        foreach (var e in graph.Edges)
        {
            _db.FlowEdges.Add(new FlowEdge
            {
                FlowVersionId = versionId,
                EdgeKey = e.EdgeKey,
                SourceNodeKey = e.SourceNodeKey,
                TargetNodeKey = e.TargetNodeKey,
                SourceHandle = e.SourceHandle,
                Label = e.Label
            });
        }

        foreach (var f in graph.Formulas)
        {
            _db.Formulas.Add(new Formula
            {
                FlowVersionId = versionId,
                Key = f.Key,
                Label = f.Label,
                Expression = f.Expression
            });
        }

        foreach (var f in graph.InputFields ?? Array.Empty<GraphInputField>())
        {
            _db.InputFields.Add(new InputField
            {
                FlowVersionId = versionId,
                Name = f.Name,
                Label = f.Label,
                Type = f.Type,
                Required = f.Required,
                Order = f.Order
            });
        }

        await _db.SaveChangesAsync(ct);

        var reloaded = await LoadVersionWithGraph(flowId, versionId, tracking: false, ct);
        var savedFormulas = await LoadFormulas(versionId, ct);
        return OperationResult<VersionGraph>.Ok(ToGraph(reloaded!, savedFormulas));
    }

    public async Task<OperationResult<FlowVersionSummary>> CreateVersionAsync(Guid flowId, Guid? copyFromVersionId, CancellationToken ct = default)
    {
        var flow = await _db.DecisionFlows.Include(f => f.Versions).FirstOrDefaultAsync(f => f.Id == flowId, ct);
        if (flow is null)
        {
            return OperationResult<FlowVersionSummary>.NotFound($"Fluxo {flowId} não encontrado.");
        }

        var nextNumber = flow.Versions.Count == 0 ? 1 : flow.Versions.Max(v => v.VersionNumber) + 1;
        var newVersion = new FlowVersion
        {
            DecisionFlowId = flowId,
            VersionNumber = nextNumber,
            Status = FlowVersionStatus.Draft
        };
        _db.FlowVersions.Add(newVersion);
        await _db.SaveChangesAsync(ct);

        if (copyFromVersionId is not null)
        {
            var source = await LoadVersionWithGraph(flowId, copyFromVersionId.Value, tracking: false, ct);
            if (source is not null)
            {
                var sourceFormulas = await LoadFormulas(copyFromVersionId.Value, ct);
                await SaveVersionGraphAsync(flowId, newVersion.Id, ToGraph(source, sourceFormulas), ct);
            }
        }

        return OperationResult<FlowVersionSummary>.Ok(ToVersionSummary(newVersion));
    }

    public async Task<OperationResult<FlowVersionSummary>> PublishVersionAsync(Guid flowId, Guid versionId, CancellationToken ct = default)
    {
        var version = await LoadVersionWithGraph(flowId, versionId, tracking: true, ct);
        if (version is null)
        {
            return OperationResult<FlowVersionSummary>.NotFound($"Versão {versionId} não encontrada no fluxo {flowId}.");
        }
        if (version.Status == FlowVersionStatus.Published)
        {
            return OperationResult<FlowVersionSummary>.Conflict("Esta versão já está publicada.");
        }

        // Validate by compiling the graph (exactly one Start + all formulas valid
        // + variable ordering/cycle detection). Variables are stored separately,
        // so load them into the snapshot for validation. Global variables are
        // merged in (local variables win on key collisions) so validation matches
        // exactly what the engine will compile at runtime.
        var localVariables = await _db.Formulas
            .AsNoTracking()
            .Where(f => f.FlowVersionId == versionId)
            .Select(f => new PublishedFormula(f.Key, f.Label, f.Expression))
            .ToListAsync(ct);

        var globalVariables = await _db.GlobalVariables
            .AsNoTracking()
            .Select(g => new PublishedFormula(g.Key, g.Label, g.Expression))
            .ToListAsync(ct);

        var variables = VariableMerge.Merge(globalVariables, localVariables);

        var snapshot = ToSnapshot(flowId, version, variables);
        try
        {
            CompiledFlow.Compile(snapshot);
        }
        catch (FlowCompilationException ex)
        {
            return OperationResult<FlowVersionSummary>.Invalid(ex.Message);
        }

        // Archive any currently published version of this flow.
        var previouslyPublished = await _db.FlowVersions
            .Where(v => v.DecisionFlowId == flowId && v.Status == FlowVersionStatus.Published)
            .ToListAsync(ct);
        foreach (var prev in previouslyPublished)
        {
            prev.Status = FlowVersionStatus.Archived;
        }

        version.Status = FlowVersionStatus.Published;
        version.PublishedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        // The published version changed: drop cached compiled flow.
        _compiledFlows.Invalidate(flowId);

        return OperationResult<FlowVersionSummary>.Ok(ToVersionSummary(version));
    }

    public async Task<OperationResult<bool>> DeleteFlowAsync(Guid flowId, CancellationToken ct = default)
    {
        var flow = await _db.DecisionFlows
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Id == flowId, ct);

        if (flow is null)
        {
            return OperationResult<bool>.NotFound($"Fluxo {flowId} não encontrado.");
        }

        // Decision executions reference flow_versions with Restrict, so remove the
        // executions (and their traces, which cascade) before deleting the flow.
        var versionIds = flow.Versions.Select(v => v.Id).ToList();
        if (versionIds.Count > 0)
        {
            var executions = await _db.DecisionExecutions
                .Where(e => versionIds.Contains(e.FlowVersionId))
                .ToListAsync(ct);
            _db.DecisionExecutions.RemoveRange(executions);
        }

        // Removing the flow cascades to versions -> nodes/edges/rulesets/rules/
        // formulas/input_fields (all configured with cascade delete).
        _db.DecisionFlows.Remove(flow);
        await _db.SaveChangesAsync(ct);

        _compiledFlows.Invalidate(flowId);

        return OperationResult<bool>.Ok(true);
    }

    // --- Loading helpers --------------------------------------------------

    private async Task<FlowVersion?> LoadVersionWithGraph(Guid flowId, Guid versionId, bool tracking, CancellationToken ct)
    {
        IQueryable<FlowVersion> query = _db.FlowVersions
            .Include(v => v.Nodes)
            .Include(v => v.Edges)
            .Include(v => v.Rulesets)
                .ThenInclude(r => r.Rules)
            .Include(v => v.InputFields);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(v => v.Id == versionId && v.DecisionFlowId == flowId, ct);
    }

    private async Task<List<GraphFormula>> LoadFormulas(Guid versionId, CancellationToken ct)
    {
        return await _db.Formulas
            .AsNoTracking()
            .Where(f => f.FlowVersionId == versionId)
            .Select(f => new GraphFormula(f.Key, f.Label, f.Expression))
            .ToListAsync(ct);
    }

    // --- Mapping ----------------------------------------------------------

    private static FlowSummary ToSummary(DecisionFlow flow) => new(
        flow.Id, flow.Name, flow.Description, flow.IsActive,
        flow.Versions
            .OrderBy(v => v.VersionNumber)
            .Select(ToVersionSummary)
            .ToList());

    private static FlowVersionSummary ToVersionSummary(FlowVersion v)
        => new(v.Id, v.VersionNumber, v.Status, v.PublishedAt, v.CreatedAt, v.UpdatedAt);

    private static VersionGraph ToGraph(FlowVersion version, IReadOnlyList<GraphFormula> formulas)
    {
        // Map ruleset id -> a stable client key (use the id string).
        var keyByRulesetId = version.Rulesets.ToDictionary(r => r.Id, r => r.Id.ToString());

        var nodes = version.Nodes.Select(n => new GraphNode(
            n.NodeKey, n.Kind, n.Label, n.PositionX, n.PositionY, n.Config,
            n.RulesetId is not null && keyByRulesetId.TryGetValue(n.RulesetId.Value, out var k) ? k : null)).ToList();

        var edges = version.Edges.Select(e => new GraphEdge(
            e.EdgeKey, e.SourceNodeKey, e.TargetNodeKey, e.SourceHandle, e.Label)).ToList();

        var rulesets = version.Rulesets.Select(r => new GraphRuleset(
            r.Id.ToString(), r.Name, r.Description, r.ApprovalThreshold,
            r.Rules.OrderBy(x => x.Order).Select(x => new GraphRule(
                x.Order, x.Name, x.ConditionExpression, x.Effect, x.ScoreWeight,
                x.ForcedOutcome, x.Message, x.IsEnabled)).ToList())).ToList();

        var inputFields = version.InputFields
            .OrderBy(f => f.Order)
            .Select(f => new GraphInputField(f.Name, f.Label, f.Type, f.Required, f.Order))
            .ToList();

        return new VersionGraph(nodes, edges, rulesets, formulas, inputFields);
    }

    /// <summary>
    /// Builds an execution snapshot from a Draft/any version, purely for
    /// validation at publish time (does not touch the cache/loader).
    /// </summary>
    private static PublishedFlowSnapshot ToSnapshot(Guid flowId, FlowVersion version, IReadOnlyList<PublishedFormula> formulas)
    {
        var nodes = version.Nodes.Select(n => new PublishedNode(
            n.NodeKey, n.Kind, n.Label, n.Config, n.RulesetId)).ToList();

        var edges = version.Edges.Select(e => new PublishedEdge(
            e.EdgeKey, e.SourceNodeKey, e.TargetNodeKey, e.SourceHandle, e.Label)).ToList();

        var rulesets = version.Rulesets.Select(r => new PublishedRuleset(
            r.Id, r.Name, r.ApprovalThreshold,
            r.Rules.OrderBy(x => x.Order).Select(x => new PublishedRule(
                x.Id, x.Order, x.Name, x.ConditionExpression, x.Effect, x.ScoreWeight,
                x.ForcedOutcome, x.Message)).ToList())).ToList();

        return new PublishedFlowSnapshot(
            flowId, string.Empty, version.Id, version.VersionNumber,
            version.PublishedAt ?? DateTime.UtcNow,
            nodes, edges, rulesets, formulas);
    }
}
