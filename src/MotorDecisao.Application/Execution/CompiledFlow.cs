using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.PublishedFlows;
using MotorDecisao.Domain.Enums;

namespace MotorDecisao.Application.Execution;

/// <summary>
/// A published flow with all its expressions already parsed into
/// <see cref="CompiledFormula"/>s. Building this once (at cache load) means the
/// executor never re-parses formulas on the hot path — it only evaluates.
///
/// This wraps a <see cref="PublishedFlowSnapshot"/> rather than replacing it, so
/// the cache/loader contract stays the same.
/// </summary>
public sealed class CompiledFlow
{
    public PublishedFlowSnapshot Snapshot { get; }

    /// <summary>Nodes indexed by their key for O(1) traversal.</summary>
    public IReadOnlyDictionary<string, PublishedNode> NodesByKey { get; }

    /// <summary>Compiled condition formula per Condition node key.</summary>
    public IReadOnlyDictionary<string, CompiledFormula> ConditionByNode { get; }

    /// <summary>Compiled assignments per Computation node key (in order).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<CompiledAssignment>> ComputationByNode { get; }

    /// <summary>Compiled actions per Action node key (in order).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<CompiledAction>> ActionsByNode { get; }

    /// <summary>
    /// Compiled actions attached to any non-Condition node (Computation, Decision,
    /// DataSource…), applied when the executor passes through the node.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<CompiledAction>> NodeActionsByNode { get; }

    /// <summary>
    /// Compiled actions attached to a Condition node, per branch. Applied after
    /// the condition is evaluated, before following the chosen edge.
    /// </summary>
    public IReadOnlyDictionary<string, CompiledBranchActions> ConditionActionsByNode { get; }

    /// <summary>Compiled matrix (config + row/col expressions) per Matrix node key.</summary>
    public IReadOnlyDictionary<string, CompiledMatrix> MatrixByNode { get; }

    /// <summary>Compiled rule conditions per ruleset id.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<CompiledRule>> RulesByRulesetId { get; }

    /// <summary>The single Start node key (entry point).</summary>
    public string StartNodeKey { get; }

    /// <summary>
    /// All distinct external references used anywhere in the flow's formulas
    /// (conditions, computations, rules). The executor pre-fetches these once per
    /// decision before evaluating.
    /// </summary>
    public IReadOnlyList<Sources.ExternalRef> ExternalReferences { get; }

    /// <summary>
    /// The flow's variables (named formulas) compiled and ordered so each is
    /// resolved after the variables it depends on. The executor evaluates these in
    /// order into the context before walking the graph.
    /// </summary>
    public IReadOnlyList<CompiledVariable> OrderedVariables { get; }

    private CompiledFlow(
        PublishedFlowSnapshot snapshot,
        IReadOnlyDictionary<string, PublishedNode> nodesByKey,
        IReadOnlyDictionary<string, CompiledFormula> conditionByNode,
        IReadOnlyDictionary<string, IReadOnlyList<CompiledAssignment>> computationByNode,
        IReadOnlyDictionary<string, IReadOnlyList<CompiledAction>> actionsByNode,
        IReadOnlyDictionary<string, IReadOnlyList<CompiledAction>> nodeActionsByNode,
        IReadOnlyDictionary<string, CompiledBranchActions> conditionActionsByNode,
        IReadOnlyDictionary<string, CompiledMatrix> matrixByNode,
        IReadOnlyDictionary<Guid, IReadOnlyList<CompiledRule>> rulesByRulesetId,
        string startNodeKey,
        IReadOnlyList<Sources.ExternalRef> externalReferences,
        IReadOnlyList<CompiledVariable> orderedVariables)
    {
        Snapshot = snapshot;
        NodesByKey = nodesByKey;
        ConditionByNode = conditionByNode;
        ComputationByNode = computationByNode;
        ActionsByNode = actionsByNode;
        NodeActionsByNode = nodeActionsByNode;
        ConditionActionsByNode = conditionActionsByNode;
        MatrixByNode = matrixByNode;
        RulesByRulesetId = rulesByRulesetId;
        StartNodeKey = startNodeKey;
        ExternalReferences = externalReferences;
        OrderedVariables = orderedVariables;
    }

    /// <summary>
    /// Compiles all expressions in a snapshot. Throws
    /// <see cref="FlowCompilationException"/> if the graph is structurally invalid
    /// (no Start / multiple Starts) or an expression fails to parse — these are
    /// authoring problems that must surface before publishing.
    /// </summary>
    public static CompiledFlow Compile(PublishedFlowSnapshot snapshot)
    {
        var nodesByKey = new Dictionary<string, PublishedNode>(StringComparer.Ordinal);
        foreach (var node in snapshot.Nodes)
        {
            nodesByKey[node.NodeKey] = node;
        }

        var starts = snapshot.Nodes.Where(n => n.Kind == FlowNodeKind.Start).ToList();
        if (starts.Count != 1)
        {
            throw new FlowCompilationException(
                $"O fluxo deve ter exatamente um nó inicial (encontrados {starts.Count}).");
        }

        var conditionByNode = new Dictionary<string, CompiledFormula>(StringComparer.Ordinal);
        var computationByNode = new Dictionary<string, IReadOnlyList<CompiledAssignment>>(StringComparer.Ordinal);
        var actionsByNode = new Dictionary<string, IReadOnlyList<CompiledAction>>(StringComparer.Ordinal);
        var nodeActionsByNode = new Dictionary<string, IReadOnlyList<CompiledAction>>(StringComparer.Ordinal);
        var conditionActionsByNode = new Dictionary<string, CompiledBranchActions>(StringComparer.Ordinal);
        var matrixByNode = new Dictionary<string, CompiledMatrix>(StringComparer.Ordinal);

        // Compila uma lista de ações (pode ser null/vazia) atrelada a um nó.
        List<CompiledAction> CompileActions(IReadOnlyList<ActionItem>? items, string where)
        {
            var list = new List<CompiledAction>();
            if (items is null) return list;
            foreach (var a in items)
            {
                list.Add(new CompiledAction(a.Type, a.Name, CompileOrThrow(a.Expression, where)));
            }
            return list;
        }

        foreach (var node in snapshot.Nodes)
        {
            switch (node.Kind)
            {
                case FlowNodeKind.Condition:
                {
                    var cfg = NodeConfig.Deserialize<ConditionConfig>(node.Config);
                    conditionByNode[node.NodeKey] = CompileOrThrow(cfg.Expression, node.NodeKey);
                    conditionActionsByNode[node.NodeKey] = new CompiledBranchActions(
                        CompileActions(cfg.TrueActions, $"ações (verdadeiro) em '{node.Label}'"),
                        CompileActions(cfg.FalseActions, $"ações (falso) em '{node.Label}'"));
                    break;
                }
                case FlowNodeKind.Computation:
                {
                    var cfg = NodeConfig.Deserialize<ComputationConfig>(node.Config);
                    var list = new List<CompiledAssignment>();
                    foreach (var a in cfg.Assignments)
                    {
                        list.Add(new CompiledAssignment(a.TargetField, CompileOrThrow(a.Expression, node.NodeKey)));
                    }
                    computationByNode[node.NodeKey] = list;
                    nodeActionsByNode[node.NodeKey] = CompileActions(cfg.Actions, $"ações em '{node.Label}'");
                    break;
                }
                case FlowNodeKind.Decision:
                {
                    var cfg = NodeConfig.Deserialize<DecisionConfig>(node.Config);
                    nodeActionsByNode[node.NodeKey] = CompileActions(cfg.Actions, $"ações em '{node.Label}'");
                    break;
                }
                case FlowNodeKind.DataSource:
                {
                    var cfg = NodeConfig.Deserialize<DataSourceConfig>(node.Config);
                    nodeActionsByNode[node.NodeKey] = CompileActions(cfg.Actions, $"ações em '{node.Label}'");
                    break;
                }
                case FlowNodeKind.Action:
                {
                    var cfg = NodeConfig.Deserialize<ActionsConfig>(node.Config);
                    actionsByNode[node.NodeKey] = CompileActions(cfg.Actions, $"ação em '{node.Label}'");
                    break;
                }
                case FlowNodeKind.Matrix:
                {
                    var cfg = NodeConfig.Deserialize<MatrixConfig>(node.Config);
                    matrixByNode[node.NodeKey] = new CompiledMatrix(
                        cfg,
                        CompileOrThrow(cfg.RowExpression, $"linha da matriz '{node.Label}'"),
                        CompileOrThrow(cfg.ColExpression, $"coluna da matriz '{node.Label}'"));
                    break;
                }
            }
        }

        var rulesByRulesetId = new Dictionary<Guid, IReadOnlyList<CompiledRule>>();
        foreach (var ruleset in snapshot.Rulesets)
        {
            var compiled = new List<CompiledRule>();
            foreach (var rule in ruleset.Rules)
            {
                compiled.Add(new CompiledRule(rule, CompileOrThrow(rule.ConditionExpression, $"regra '{rule.Name}'")));
            }
            rulesByRulesetId[ruleset.RulesetId] = compiled;
        }

        // Variables (the flow's named formulas). Compile each, then order them so
        // a variable is resolved after the variables it references, detecting cycles.
        var compiledByName = new Dictionary<string, CompiledFormula>(StringComparer.OrdinalIgnoreCase);
        foreach (var formula in snapshot.Formulas)
        {
            compiledByName[formula.Key] = CompileOrThrow(formula.Expression, $"variável '{formula.Key}'");
        }
        var orderedVariables = OrderVariables(compiledByName);

        // Aggregate every external reference used across all compiled formulas
        // (including variables) so the executor pre-fetches them once per decision.
        var externalRefs = new List<Sources.ExternalRef>();
        void Collect(CompiledFormula f)
        {
            foreach (var r in f.ExternalReferences)
            {
                if (!externalRefs.Contains(r)) externalRefs.Add(r);
            }
        }
        foreach (var c in conditionByNode.Values) Collect(c);
        foreach (var list in computationByNode.Values)
            foreach (var a in list) Collect(a.Formula);
        foreach (var list in actionsByNode.Values)
            foreach (var a in list) Collect(a.Formula);
        foreach (var list in nodeActionsByNode.Values)
            foreach (var a in list) Collect(a.Formula);
        foreach (var branch in conditionActionsByNode.Values)
        {
            foreach (var a in branch.TrueActions) Collect(a.Formula);
            foreach (var a in branch.FalseActions) Collect(a.Formula);
        }
        foreach (var m in matrixByNode.Values)
        {
            Collect(m.RowFormula);
            Collect(m.ColFormula);
        }
        foreach (var list in rulesByRulesetId.Values)
            foreach (var cr in list) Collect(cr.Condition);
        foreach (var v in orderedVariables) Collect(v.Formula);

        return new CompiledFlow(
            snapshot, nodesByKey, conditionByNode, computationByNode, actionsByNode,
            nodeActionsByNode, conditionActionsByNode, matrixByNode,
            rulesByRulesetId, starts[0].NodeKey, externalRefs, orderedVariables);
    }

    /// <summary>
    /// Topologically orders the variables by their <c>{var}</c> dependencies so
    /// each is evaluated after those it references. Throws on a cycle or a
    /// reference to an unknown variable (authoring errors caught at publish).
    /// </summary>
    private static IReadOnlyList<CompiledVariable> OrderVariables(
        IReadOnlyDictionary<string, CompiledFormula> compiledByName)
    {
        var ordered = new List<CompiledVariable>();
        // 0 = unvisited, 1 = visiting (on the stack), 2 = done.
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void Visit(string name, Stack<string> path)
        {
            if (state.TryGetValue(name, out var s))
            {
                if (s == 1)
                {
                    var cycle = string.Join(" → ", path.Reverse().Append(name).Select(n => "{" + n + "}"));
                    throw new FlowCompilationException($"Ciclo entre variáveis: {cycle}.");
                }
                if (s == 2) return;
            }

            state[name] = 1;
            path.Push(name);

            var formula = compiledByName[name];
            foreach (var dep in formula.ReferencedVariables)
            {
                if (!compiledByName.ContainsKey(dep))
                {
                    throw new FlowCompilationException(
                        $"Variável '{name}' referencia uma variável inexistente: {{{dep}}}.");
                }
                Visit(dep, path);
            }

            path.Pop();
            state[name] = 2;
            ordered.Add(new CompiledVariable(name, formula));
        }

        foreach (var name in compiledByName.Keys)
        {
            Visit(name, new Stack<string>());
        }

        return ordered;
    }

    private static CompiledFormula CompileOrThrow(string expression, string where)
    {
        if (!FormulaEngine.TryCompile(expression, out var formula, out var error))
        {
            throw new FlowCompilationException(
                $"Fórmula inválida em {where}: {error!.Message} (posição {error.Position}).");
        }
        return formula!;
    }
}

/// <summary>A named variable with its compiled formula (resolution unit).</summary>
public sealed record CompiledVariable(string Name, CompiledFormula Formula);

/// <summary>A computation assignment with its compiled formula.</summary>
public sealed record CompiledAssignment(string TargetField, CompiledFormula Formula);

/// <summary>A single action with its compiled expression.</summary>
public sealed record CompiledAction(ActionType Type, string? Name, CompiledFormula Formula);

/// <summary>Actions attached to a Condition node, split by branch (V/F).</summary>
public sealed record CompiledBranchActions(
    IReadOnlyList<CompiledAction> TrueActions,
    IReadOnlyList<CompiledAction> FalseActions);

/// <summary>A matrix node's config with its compiled row/column expressions.</summary>
public sealed record CompiledMatrix(MatrixConfig Config, CompiledFormula RowFormula, CompiledFormula ColFormula);

/// <summary>A rule paired with its compiled condition formula.</summary>
public sealed record CompiledRule(PublishedRule Rule, CompiledFormula Condition);

/// <summary>Raised when a published flow cannot be compiled (authoring error).</summary>
public sealed class FlowCompilationException : Exception
{
    public FlowCompilationException(string message) : base(message) { }
}
