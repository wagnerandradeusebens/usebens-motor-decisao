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
    private readonly IPublishedFlowLoader _loader;

    public FlowManagementService(
        MotorDecisaoDbContext db,
        ICompiledFlowProvider compiledFlows,
        IPublishedFlowLoader loader)
    {
        _db = db;
        _compiledFlows = compiledFlows;
        _loader = loader;
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

        if (flow is null)
        {
            return OperationResult<FlowSummary>.NotFound($"Fluxo {flowId} não encontrado.");
        }

        var links = await LoadVersionLinksAsync(flowId, ct);
        return OperationResult<FlowSummary>.Ok(ToSummary(flow, links));
    }

    /// <summary>
    /// Para cada versão deste fluxo, quais OUTRAS políticas (bundles publicados
    /// ativos, de RootFlowId diferente) a congelam como membro — o "Vinculado a"
    /// do histórico. Mesma varredura em memória de <see cref="IsVersionFrozenAsync"/>:
    /// há poucos bundles ativos, então percorrer o manifesto (MembersJson) é barato.
    /// Usa o nome do bundle (RootFlowId) na tabela de fluxos (nome atual).
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> LoadVersionLinksAsync(
        Guid flowId, CancellationToken ct)
    {
        var bundles = await _db.PublishedBundles
            .AsNoTracking()
            .Where(b => b.IsActive && b.RootFlowId != flowId)
            .Select(b => new { b.RootFlowId, b.MembersJson })
            .ToListAsync(ct);

        if (bundles.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<string>>();
        }

        var names = await _db.DecisionFlows
            .AsNoTracking()
            .ToDictionaryAsync(f => f.Id, f => f.Name, ct);

        // versão desta política -> nomes das políticas que a referenciam.
        var byVersion = new Dictionary<Guid, List<string>>();
        foreach (var bundle in bundles)
        {
            var rootName = names.TryGetValue(bundle.RootFlowId, out var n) ? n : bundle.RootFlowId.ToString();
            foreach (var member in BundleJson.DeserializeMembers(bundle.MembersJson))
            {
                if (!byVersion.TryGetValue(member.FlowVersionId, out var list))
                {
                    byVersion[member.FlowVersionId] = list = new List<string>();
                }
                if (!list.Contains(rootName))
                {
                    list.Add(rootName);
                }
            }
        }

        return byVersion.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);
    }

    public async Task<OperationResult<VersionGraph>> GetVersionGraphAsync(Guid flowId, Guid versionId, CancellationToken ct = default)
    {
        var version = await LoadVersionWithGraph(flowId, versionId, tracking: false, ct);
        if (version is null)
        {
            return OperationResult<VersionGraph>.NotFound($"Versão {versionId} não encontrada no fluxo {flowId}.");
        }
        var formulas = await LoadFormulas(versionId, ct);
        var tables = await LoadTables(versionId, ct);
        return OperationResult<VersionGraph>.Ok(ToGraph(version, formulas, tables));
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
        // Mesmo em rascunho, uma versão congelada num bundle publicado (ex.: uma
        // subpolítica de uma principal já publicada) não pode ser editada — crie
        // uma nova versão. Isso preserva a imutabilidade do que está em produção.
        if (await IsVersionFrozenAsync(versionId, ct))
        {
            return OperationResult<VersionGraph>.Conflict(
                "Esta versão está congelada em uma política publicada. Crie uma nova versão para editar.");
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
        var oldTables = await _db.ParameterTables.Where(t => t.FlowVersionId == versionId).ToListAsync(ct);
        _db.ParameterTables.RemoveRange(oldTables);

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

        foreach (var t in graph.Tables ?? Array.Empty<GraphTable>())
        {
            _db.ParameterTables.Add(new ParameterTable
            {
                FlowVersionId = versionId,
                Name = t.Name,
                Label = t.Label,
                ColumnsJson = ParameterTableJson.SerializeColumns(t.Columns),
                RowsJson = ParameterTableJson.SerializeRows(t.Rows),
                KeyColumn = t.KeyColumn,
                MinColumn = t.MinColumn,
                MaxColumn = t.MaxColumn,
                DefaultValue = t.DefaultValue
            });
        }

        await _db.SaveChangesAsync(ct);

        var reloaded = await LoadVersionWithGraph(flowId, versionId, tracking: false, ct);
        var savedFormulas = await LoadFormulas(versionId, ct);
        var savedTables = await LoadTables(versionId, ct);
        var saved = ToGraph(reloaded!, savedFormulas, savedTables);

        // Validação best-effort (não bloqueia): compila as fórmulas e checa PROCV
        // contra a config das tabelas; devolve avisos para o editor exibir.
        var warnings = VersionGraphValidator.Validate(saved, await LoadGlobalTableShapesAsync(ct));
        return OperationResult<VersionGraph>.Ok(saved with { Warnings = warnings });
    }

    /// <summary>Config mínima das tabelas globais para validar chamadas de PROCV.</summary>
    private async Task<IReadOnlyList<TableShape>> LoadGlobalTableShapesAsync(CancellationToken ct)
    {
        var globals = await _db.GlobalParameterTables
            .AsNoTracking()
            .Select(t => new { t.Name, t.ColumnsJson, t.KeyColumn, t.MinColumn, t.MaxColumn })
            .ToListAsync(ct);

        return globals.Select(g => new TableShape(
            g.Name,
            ParameterTableJson.DeserializeColumns(g.ColumnsJson).Select(c => c.Name).ToList(),
            g.KeyColumn,
            g.MinColumn,
            g.MaxColumn)).ToList();
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
                var sourceTables = await LoadTables(copyFromVersionId.Value, ct);
                await SaveVersionGraphAsync(flowId, newVersion.Id, ToGraph(source, sourceFormulas, sourceTables), ct);
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

        // Congela o bundle: a principal recém-publicada + todas as subpolíticas
        // referenciadas (recursivamente), cada uma na sua versão mais recente
        // AGORA. Se uma sub referenciada não existir, a publicação falha com
        // mensagem clara. Feito após o SaveChanges para o loader enxergar a
        // principal já publicada.
        var bundleResult = await BuildAndSaveBundleAsync(flowId, version.Id, ct);
        if (bundleResult is not null)
        {
            // Reverte a publicação se o congelamento falhou (ex.: sub inexistente).
            version.Status = FlowVersionStatus.Draft;
            version.PublishedAt = null;
            foreach (var prev in previouslyPublished)
            {
                prev.Status = FlowVersionStatus.Published;
            }
            await _db.SaveChangesAsync(ct);
            return OperationResult<FlowVersionSummary>.Invalid(bundleResult);
        }

        // The published version changed: drop cached compiled flow.
        _compiledFlows.Invalidate(flowId);

        return OperationResult<FlowVersionSummary>.Ok(ToVersionSummary(version));
    }

    public async Task<OperationResult<int>> BackfillBundlesAsync(CancellationToken ct = default)
    {
        // Políticas com versão publicada.
        var publishedVersions = await _db.FlowVersions
            .AsNoTracking()
            .Where(v => v.Status == FlowVersionStatus.Published)
            .Select(v => new { v.DecisionFlowId, v.Id })
            .ToListAsync(ct);

        // As que já têm bundle ativo (pular).
        var withBundle = await _db.PublishedBundles
            .AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => b.RootFlowId)
            .ToListAsync(ct);
        var withBundleSet = new HashSet<Guid>(withBundle);

        var generated = 0;
        foreach (var v in publishedVersions)
        {
            if (withBundleSet.Contains(v.DecisionFlowId)) continue;
            var error = await BuildAndSaveBundleAsync(v.DecisionFlowId, v.Id, ct);
            if (error is null)
            {
                generated++;
                _compiledFlows.Invalidate(v.DecisionFlowId);
            }
            // Se falhar (ex.: sub inexistente), não interrompe as demais.
        }
        return OperationResult<int>.Ok(generated);
    }

    /// <summary>
    /// Monta e grava o <see cref="PublishedBundle"/> da publicação: congela a
    /// principal (versão publicada) + todas as subpolíticas referenciadas em
    /// cascata (versão mais recente no instante). Desativa o bundle anterior da
    /// principal. Retorna null em sucesso, ou uma mensagem de erro (pt-BR) quando
    /// uma subpolítica referenciada não existe.
    /// </summary>
    private async Task<string?> BuildAndSaveBundleAsync(Guid rootFlowId, Guid rootVersionId, CancellationToken ct)
    {
        var snapshots = new Dictionary<Guid, PublishedFlowSnapshot>();
        var members = new List<BundleMember>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // por nome, corta ciclos

        // Fila de trabalho: começa pela principal (por id, já publicada).
        var root = await _loader.LoadAsync(rootFlowId, ct);
        if (root is null)
        {
            return "Não foi possível montar o snapshot da política publicada.";
        }
        visited.Add(root.FlowName);
        var queue = new Queue<PublishedFlowSnapshot>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var snap = queue.Dequeue();
            snapshots[snap.FlowId] = snap;
            members.Add(new BundleMember(snap.FlowId, snap.FlowVersionId, snap.FlowName));

            // Descobre as subpolíticas referenciadas compilando o snapshot.
            IReadOnlyList<string> referenced;
            try
            {
                referenced = CompiledFlow.Compile(snap).ReferencedPolicies;
            }
            catch (FlowCompilationException)
            {
                referenced = Array.Empty<string>();
            }

            foreach (var name in referenced)
            {
                if (!visited.Add(name)) continue; // já congelada nesta cascata
                var sub = await _loader.LoadLatestByNameAsync(name, ct);
                if (sub is null)
                {
                    return $"A política referenciada '{name}' não existe e não pôde ser congelada.";
                }
                queue.Enqueue(sub);
            }
        }

        // Desativa o bundle ativo anterior da principal (histórico preservado).
        var previous = await _db.PublishedBundles
            .Where(b => b.RootFlowId == rootFlowId && b.IsActive)
            .ToListAsync(ct);
        foreach (var b in previous) b.IsActive = false;

        var bundle = new PublishedBundle
        {
            RootFlowId = rootFlowId,
            RootFlowVersionId = rootVersionId,
            PublishedAt = DateTime.UtcNow,
            IsActive = true,
            SnapshotsJson = BundleJson.SerializeSnapshots(snapshots),
            MembersJson = BundleJson.SerializeMembers(members),
        };
        _db.PublishedBundles.Add(bundle);
        await _db.SaveChangesAsync(ct);
        return null;
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

    public async Task<OperationResult<bool>> DeleteVersionAsync(Guid flowId, Guid versionId, CancellationToken ct = default)
    {
        var flow = await _db.DecisionFlows
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Id == flowId, ct);

        if (flow is null)
        {
            return OperationResult<bool>.NotFound($"Fluxo {flowId} não encontrado.");
        }

        var version = flow.Versions.FirstOrDefault(v => v.Id == versionId);
        if (version is null)
        {
            return OperationResult<bool>.NotFound($"Versão {versionId} não encontrada no fluxo {flowId}.");
        }

        // Não exclui versão publicada: ela é (ou foi) o que rodou em produção.
        if (version.Status == FlowVersionStatus.Published)
        {
            return OperationResult<bool>.Conflict(
                "Não é possível excluir uma versão publicada. Publique outra versão ou arquive esta antes.");
        }

        // Não exclui versão congelada em bundle ativo (vinculada a outra política).
        if (await IsVersionFrozenAsync(versionId, ct))
        {
            return OperationResult<bool>.Conflict(
                "Não é possível excluir esta versão: ela está congelada na publicação de outra política. " +
                "Publique uma nova versão da política principal para desvinculá-la.");
        }

        // Não deixa o fluxo sem nenhuma versão — nesse caso, exclua a política.
        if (flow.Versions.Count <= 1)
        {
            return OperationResult<bool>.Conflict(
                "Não é possível excluir a única versão da política. Exclua a política inteira.");
        }

        // Execuções referenciam a versão com Restrict; remova-as (e as trilhas, que
        // cascateiam) antes de excluir a versão.
        var executions = await _db.DecisionExecutions
            .Where(e => e.FlowVersionId == versionId)
            .ToListAsync(ct);
        _db.DecisionExecutions.RemoveRange(executions);

        // A remoção da versão cascateia para nós/arestas/rulesets/regras/fórmulas/
        // campos de entrada/tabelas de parâmetros (todos configurados com cascade).
        _db.FlowVersions.Remove(version);
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

    /// <summary>
    /// Verdadeiro se a versão participa de algum bundle publicado ativo (como
    /// principal ou subpolítica congelada). Nesses casos a edição é bloqueada;
    /// o usuário deve criar uma nova versão. São poucos bundles ativos, então a
    /// varredura em memória do manifesto (MembersJson) é barata.
    /// </summary>
    private async Task<bool> IsVersionFrozenAsync(Guid versionId, CancellationToken ct)
    {
        var manifests = await _db.PublishedBundles
            .AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => b.MembersJson)
            .ToListAsync(ct);

        foreach (var json in manifests)
        {
            var members = BundleJson.DeserializeMembers(json);
            if (members.Any(m => m.FlowVersionId == versionId))
            {
                return true;
            }
        }
        return false;
    }

    private async Task<List<GraphTable>> LoadTables(Guid versionId, CancellationToken ct)
    {
        var entities = await _db.ParameterTables
            .AsNoTracking()
            .Where(t => t.FlowVersionId == versionId)
            .ToListAsync(ct);

        return entities.Select(t => new GraphTable(
            t.Name,
            t.Label,
            ParameterTableJson.DeserializeColumns(t.ColumnsJson),
            ParameterTableJson.DeserializeRows(t.RowsJson),
            t.KeyColumn,
            t.MinColumn,
            t.MaxColumn,
            t.DefaultValue)).ToList();
    }



    // --- Mapping ----------------------------------------------------------

    /// <summary>
    /// Mapeia sem os vínculos "Vinculado a" (usado na listagem, onde a coluna
    /// não é exibida e uma varredura por fluxo seria custosa).
    /// </summary>
    private static FlowSummary ToSummary(DecisionFlow flow)
        => ToSummary(flow, EmptyLinks);

    private static readonly IReadOnlyDictionary<Guid, IReadOnlyList<string>> EmptyLinks
        = new Dictionary<Guid, IReadOnlyList<string>>();

    private static FlowSummary ToSummary(
        DecisionFlow flow, IReadOnlyDictionary<Guid, IReadOnlyList<string>> links) => new(
        flow.Id, flow.Name, flow.Description, flow.IsActive,
        flow.Versions
            .OrderBy(v => v.VersionNumber)
            .Select(v => ToVersionSummary(v, links))
            .ToList());

    /// <summary>Mapeia sem vínculos (versão recém-criada/publicada — ainda sem "Vinculado a").</summary>
    private static FlowVersionSummary ToVersionSummary(FlowVersion v)
        => ToVersionSummary(v, EmptyLinks);

    private static FlowVersionSummary ToVersionSummary(
        FlowVersion v, IReadOnlyDictionary<Guid, IReadOnlyList<string>> links)
        => new(v.Id, v.VersionNumber, v.Status, v.PublishedAt, v.CreatedAt, v.UpdatedAt,
            links.TryGetValue(v.Id, out var names) ? names : Array.Empty<string>());

    private static VersionGraph ToGraph(
        FlowVersion version,
        IReadOnlyList<GraphFormula> formulas,
        IReadOnlyList<GraphTable> tables)
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

        return new VersionGraph(nodes, edges, rulesets, formulas, inputFields) { Tables = tables };
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
