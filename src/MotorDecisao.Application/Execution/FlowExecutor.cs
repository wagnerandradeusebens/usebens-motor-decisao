using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.PublishedFlows;
using MotorDecisao.Application.Sources;
using MotorDecisao.Domain.Enums;

namespace MotorDecisao.Application.Execution;

/// <summary>
/// Walks a <see cref="CompiledFlow"/> against a proposal and produces a decision
/// plus a full audit trace. Pure orchestration: no database access — the caller
/// persists the result. Rules/formulas come pre-compiled, so this only evaluates.
///
/// Safety rails:
/// <list type="bullet">
/// <item>A step cap prevents an infinite walk on a malformed/cyclic graph.</item>
/// <item>A non-terminal node with no outgoing edge is a controlled failure.</item>
/// <item>A runtime formula error (data-dependent, e.g. divide-by-zero for a
/// specific proposal) routes to <see cref="DecisionOutcome.ManualReview"/> and is
/// noted in the trace rather than throwing. This is the prudent path in credit;
/// syntax errors are already caught at authoring time.</item>
/// </list>
/// </summary>
public sealed class FlowExecutor
{
    private readonly IDataSourceResolver _dataSources;
    private readonly ISourceCatalog _sources;
    private readonly int _maxSteps;

    public FlowExecutor(IDataSourceResolver dataSources, ISourceCatalog sources, int maxSteps = 1000)
    {
        _dataSources = dataSources;
        _sources = sources;
        _maxSteps = maxSteps;
    }

    public async Task<DecisionResult> ExecuteAsync(
        CompiledFlow flow,
        DecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = new DictionaryFormulaContext(request.Input);

        var trace = new List<TraceStep>();
        var sequence = 0;

        // Pre-fetch every external reference the flow uses ([Fonte;Produto;Dado])
        // once, before evaluating, since formula evaluation is synchronous. Each
        // ref is resolved a single time per decision (deduplicated in CompiledFlow).
        // Recorded in the trace so "everything consulted" is visible in the log.
        foreach (var reference in flow.ExternalReferences)
        {
            var value = await _sources.ResolveAsync(reference, context, cancellationToken);
            context.SetExternal(reference.Source, reference.Product, reference.Datum, value);
            trace.Add(new TraceStep(sequence++, "(fonte)", reference.Source,
                reference.ToString(), value.ToString(),
                $"Consulta à fonte {reference}")
            { Category = TraceCategory.Fonte });
        }

        // Resolve variables in dependency order (a variable may use fields,
        // external refs, functions, and other already-resolved variables). Traced
        // deeply so the full resolution tree is visible in the log.
        foreach (var variable in flow.OrderedVariables)
        {
            var (value, steps) = variable.Formula.EvaluateTraced(context);
            context.SetVariable(variable.Name, value);
            trace.Add(new TraceStep(sequence++, "(variável)", variable.Name,
                null, $"{{{variable.Name}}} = {value}", "Variável calculada")
            { Category = TraceCategory.Variavel, Detail = steps });
        }

        decimal score = 0m;
        decimal limit = 0m;
        var justifications = new List<string>();
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Exposes the accumulated counters as the fields 'pontos' and 'limite' so
        // formulas/actions can read them mid-run (Crivo's [Criterio Atual;...]).
        void SyncCounters()
        {
            context.Set("pontos", FormulaValue.Number(score));
            context.Set("limite", FormulaValue.Number(limit));
        }
        SyncCounters();

        var currentKey = flow.StartNodeKey;
        var visitedSteps = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++visitedSteps > _maxSteps)
            {
                return Failed(flow, score, limit, justifications, outputs, trace,
                    "Limite de passos excedido; possível ciclo no fluxo.");
            }

            if (!flow.NodesByKey.TryGetValue(currentKey, out var node))
            {
                return Failed(flow, score, limit, justifications, outputs, trace,
                    $"Nó '{currentKey}' não encontrado no fluxo.");
            }

            switch (node.Kind)
            {
                case FlowNodeKind.Start:
                    trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label, null, null, "Início")
                    { Category = TraceCategory.Inicio });
                    break;

                case FlowNodeKind.Comment:
                    // Purely visual annotation. If it ever ends up on an executed
                    // path, it has no effect and simply passes control along.
                    break;

                case FlowNodeKind.Computation:
                {
                    var assignments = flow.ComputationByNode[node.NodeKey];
                    foreach (var a in assignments)
                    {
                        var (value, steps) = a.Formula.EvaluateTraced(context);
                        if (value.IsError)
                        {
                            trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                                null, value.ToString(),
                                $"Erro ao calcular '{a.TargetField}' → revisão manual.")
                            { Detail = steps });
                            return ManualReview(flow, score, limit, justifications, outputs, trace);
                        }
                        context.Set(a.TargetField, value);
                        trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                            null, $"{a.TargetField} = {value}", "Cálculo")
                        { Detail = steps });
                    }
                    break;
                }

                case FlowNodeKind.Action:
                {
                    var actions = flow.ActionsByNode[node.NodeKey];
                    foreach (var action in actions)
                    {
                        var (value, steps) = action.Formula.EvaluateTraced(context);
                        if (value.IsError)
                        {
                            trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                                null, value.ToString(), "Erro em ação → revisão manual.")
                            { Category = TraceCategory.Acao, Detail = steps });
                            return ManualReview(flow, score, limit, justifications, outputs, trace);
                        }

                        switch (action.Type)
                        {
                            case ActionType.AddPoints:
                                score += ToNumber(value);
                                trace.Add(Step(ref sequence, node, $"pontos += {value} (total {score})", "Adiciona aos pontos", TraceCategory.Acao, steps));
                                break;
                            case ActionType.SetPoints:
                                score = ToNumber(value);
                                trace.Add(Step(ref sequence, node, $"pontos = {score}", "Define pontos", TraceCategory.Acao, steps));
                                break;
                            case ActionType.AddLimit:
                                limit += ToNumber(value);
                                trace.Add(Step(ref sequence, node, $"limite += {value} (total {limit})", "Adiciona ao limite", TraceCategory.Acao, steps));
                                break;
                            case ActionType.SetLimit:
                                limit = ToNumber(value);
                                trace.Add(Step(ref sequence, node, $"limite = {limit}", "Define limite", TraceCategory.Acao, steps));
                                break;
                            case ActionType.AddJustification:
                                justifications.Add(value.AsText());
                                trace.Add(Step(ref sequence, node, value.AsText(), "Adiciona à justificativa", TraceCategory.Acao, steps));
                                break;
                            case ActionType.SetJustification:
                                justifications.Clear();
                                justifications.Add(value.AsText());
                                trace.Add(Step(ref sequence, node, value.AsText(), "Define justificativa", TraceCategory.Acao, steps));
                                break;
                            case ActionType.SetOutput:
                                var name = action.Name ?? string.Empty;
                                outputs[name] = value.ToString();
                                trace.Add(Step(ref sequence, node, $"{name} = {value}", "Define parâmetro de saída", TraceCategory.Acao, steps));
                                break;
                        }
                        SyncCounters();
                    }
                    break;
                }

                case FlowNodeKind.Matrix:
                {
                    var matrix = flow.MatrixByNode[node.NodeKey];
                    var (rowVal, rowSteps) = matrix.RowFormula.EvaluateTraced(context);
                    var (colVal, colSteps) = matrix.ColFormula.EvaluateTraced(context);
                    var matrixSteps = new List<EvalStep>(rowSteps.Count + colSteps.Count);
                    matrixSteps.AddRange(rowSteps);
                    matrixSteps.AddRange(colSteps);
                    if (rowVal.IsError || colVal.IsError)
                    {
                        trace.Add(Step(ref sequence, node, "erro", "Erro na expressão da matriz → revisão manual.", TraceCategory.Matriz, matrixSteps));
                        return ManualReview(flow, score, limit, justifications, outputs, trace);
                    }

                    var rowNum = ToNumber(rowVal);
                    var colNum = ToNumber(colVal);
                    var rowIdx = FindBand(matrix.Config.RowBands, rowNum);
                    var colIdx = FindBand(matrix.Config.ColBands, colNum);

                    string? cell = null;
                    if (rowIdx >= 0 && colIdx >= 0
                        && rowIdx < matrix.Config.Cells.Count
                        && colIdx < matrix.Config.Cells[rowIdx].Count)
                    {
                        cell = matrix.Config.Cells[rowIdx][colIdx];
                    }
                    cell ??= matrix.Config.DefaultValue;

                    if (cell is null)
                    {
                        trace.Add(Step(ref sequence, node, $"({rowNum}, {colNum}) sem célula", "Matriz sem valor para o cruzamento → revisão manual.", TraceCategory.Matriz, matrixSteps));
                        return ManualReview(flow, score, limit, justifications, outputs, trace);
                    }

                    switch (matrix.Config.Mode)
                    {
                        case MatrixMode.Points:
                            score += ParseDecimal(cell);
                            trace.Add(Step(ref sequence, node, $"({rowNum}, {colNum}) → +{cell} pontos (total {score})", "Matriz (pontos)", TraceCategory.Matriz, matrixSteps));
                            break;
                        case MatrixMode.Limit:
                            limit += ParseDecimal(cell);
                            trace.Add(Step(ref sequence, node, $"({rowNum}, {colNum}) → +{cell} limite (total {limit})", "Matriz (limite)", TraceCategory.Matriz, matrixSteps));
                            break;
                        case MatrixMode.Decision:
                            var outcome = Enum.TryParse<DecisionOutcome>(cell, out var o) ? o : DecisionOutcome.ManualReview;
                            trace.Add(Step(ref sequence, node, $"({rowNum}, {colNum}) → {outcome}", "Matriz (decisão)", TraceCategory.Matriz, matrixSteps));
                            return Completed(flow, outcome, score, limit, justifications, outputs, trace);
                    }
                    SyncCounters();
                    break;
                }

                case FlowNodeKind.Condition:
                {
                    var formula = flow.ConditionByNode[node.NodeKey];
                    var (result, condSteps) = formula.EvaluateTraced(context);
                    if (result.IsError)
                    {
                        trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                            null, result.ToString(), "Erro na condição → revisão manual.")
                        { Category = TraceCategory.Regra, Detail = condSteps });
                        return ManualReview(flow, score, limit, justifications, outputs, trace);
                    }

                    var boolResult = FormulaCoercion.ToBoolean(result);
                    if (boolResult.IsError)
                    {
                        trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                            null, result.ToString(), "Condição não booleana → revisão manual.")
                        { Category = TraceCategory.Regra, Detail = condSteps });
                        return ManualReview(flow, score, limit, justifications, outputs, trace);
                    }

                    var branch = boolResult.AsBoolean();
                    trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                        null, branch ? "VERDADEIRO" : "FALSO", $"Desvio: {(branch ? "true" : "false")}")
                    { Category = TraceCategory.Regra, Detail = condSteps });

                    var next = FindNext(flow, node.NodeKey, branch ? "true" : "false");
                    if (next is null)
                    {
                        return Failed(flow, score, limit, justifications, outputs, trace,
                            $"Condição '{node.Label}' sem aresta de saída para '{(branch ? "true" : "false")}'.");
                    }
                    currentKey = next;
                    continue;
                }

                case FlowNodeKind.Ruleset:
                {
                    if (node.RulesetId is null || !flow.RulesByRulesetId.TryGetValue(node.RulesetId.Value, out var rules))
                    {
                        return Failed(flow, score, limit, justifications, outputs, trace,
                            $"Nó de regras '{node.Label}' sem conjunto de regras associado.");
                    }

                    foreach (var compiled in rules)
                    {
                        var rule = compiled.Rule;
                        var matched = compiled.Condition.Evaluate(context);
                        if (matched.IsError)
                        {
                            trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                                null, matched.ToString(),
                                $"Erro na regra '{rule.Name}' → revisão manual."));
                            return ManualReview(flow, score, limit, justifications, outputs, trace);
                        }

                        var boolMatched = FormulaCoercion.ToBoolean(matched);
                        if (boolMatched.IsError || !boolMatched.AsBoolean())
                        {
                            continue; // rule did not fire
                        }

                        switch (rule.Effect)
                        {
                            case RuleEffect.Score:
                                score += rule.ScoreWeight;
                                trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                                    null, $"+{rule.ScoreWeight} (total {score})",
                                    $"Regra '{rule.Name}': {rule.Message}"));
                                break;

                            case RuleEffect.Decision:
                                var forced = rule.ForcedOutcome ?? DecisionOutcome.ManualReview;
                                trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                                    null, forced.ToString(),
                                    $"Regra '{rule.Name}' forçou desfecho: {rule.Message}"));
                                return Completed(flow, forced, score, limit, justifications, outputs, trace);

                            case RuleEffect.Annotation:
                                trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                                    null, null, $"Nota da regra '{rule.Name}': {rule.Message}"));
                                break;
                        }
                    }
                    break;
                }

                case FlowNodeKind.DataSource:
                {
                    var cfg = NodeConfig.Deserialize<DataSourceConfig>(node.Config);
                    var fields = await _dataSources.ResolveAsync(cfg, context, cancellationToken);
                    foreach (var kv in fields)
                    {
                        context.Set(kv.Key, kv.Value);
                    }
                    trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                        null, $"{fields.Count} campo(s)", $"Fonte de dados: {cfg.Source}"));
                    break;
                }

                case FlowNodeKind.Decision:
                {
                    var cfg = NodeConfig.Deserialize<DecisionConfig>(node.Config);
                    trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                        null, cfg.Outcome.ToString(), cfg.Message ?? "Desfecho final"));
                    return Completed(flow, cfg.Outcome, score, limit, justifications, outputs, trace);
                }
            }

            // For sequential (single-output) nodes, follow the unlabelled edge.
            var following = FindNext(flow, currentKey, sourceHandle: null);
            if (following is null)
            {
                return Failed(flow, score, limit, justifications, outputs, trace,
                    $"Nó '{node.Label}' sem próximo nó (aresta de saída ausente).");
            }
            currentKey = following;
        }
    }

    /// <summary>
    /// Finds the target of the edge leaving <paramref name="sourceKey"/>. When
    /// <paramref name="sourceHandle"/> is given (Condition true/false), the edge
    /// must match that handle; otherwise the first edge is taken.
    /// </summary>
    private static string? FindNext(CompiledFlow flow, string sourceKey, string? sourceHandle)
    {
        foreach (var edge in flow.Snapshot.Edges)
        {
            if (!string.Equals(edge.SourceNodeKey, sourceKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (sourceHandle is null)
            {
                return edge.TargetNodeKey;
            }

            if (string.Equals(edge.SourceHandle, sourceHandle, StringComparison.OrdinalIgnoreCase))
            {
                return edge.TargetNodeKey;
            }
        }

        return null;
    }

    private static decimal ToNumber(FormulaValue v)
    {
        var n = FormulaCoercion.ToNumber(v);
        return n.IsError ? 0m : n.AsNumber();
    }

    /// <summary>Index of the first band matching <c>min &lt;= value &lt; max</c> (open ends), or -1.</summary>
    private static int FindBand(IReadOnlyList<MatrixBand> bands, decimal value)
    {
        for (var i = 0; i < bands.Count; i++)
        {
            var b = bands[i];
            if ((b.Min is null || value >= b.Min.Value) && (b.Max is null || value < b.Max.Value))
            {
                return i;
            }
        }
        return -1;
    }

    private static decimal ParseDecimal(string s)
        => FormulaCoercion.TryParseNumber(s, out var d) ? d : 0m;

    private static TraceStep Step(ref int sequence, PublishedNode node, string result, string message,
        TraceCategory category = TraceCategory.Fluxo, IReadOnlyList<EvalStep>? detail = null)
        => new(sequence++, node.NodeKey, node.Label, null, result, message)
        { Category = category, Detail = detail ?? Array.Empty<EvalStep>() };

    private static DecisionResult Completed(
        CompiledFlow flow, DecisionOutcome outcome, decimal score, decimal limit,
        List<string> justifications, Dictionary<string, string> outputs, List<TraceStep> trace)
        => new(flow.Snapshot.FlowId, flow.Snapshot.FlowVersionId, outcome, score, ExecutionStatus.Completed, null, trace)
        { Limit = limit, Justifications = justifications, Outputs = outputs };

    private static DecisionResult ManualReview(
        CompiledFlow flow, decimal score, decimal limit,
        List<string> justifications, Dictionary<string, string> outputs, List<TraceStep> trace)
        => new(flow.Snapshot.FlowId, flow.Snapshot.FlowVersionId, DecisionOutcome.ManualReview, score, ExecutionStatus.Completed, null, trace)
        { Limit = limit, Justifications = justifications, Outputs = outputs };

    private static DecisionResult Failed(
        CompiledFlow flow, decimal score, decimal limit,
        List<string> justifications, Dictionary<string, string> outputs, List<TraceStep> trace, string error)
        => new(flow.Snapshot.FlowId, flow.Snapshot.FlowVersionId, DecisionOutcome.Pending, score, ExecutionStatus.Failed, error, trace)
        { Limit = limit, Justifications = justifications, Outputs = outputs };
}
