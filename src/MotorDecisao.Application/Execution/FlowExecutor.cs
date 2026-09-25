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

    /// <summary>
    /// Provedor das políticas referenciadas por <c>(Política;...)</c>. Opcional:
    /// injetado na composição real; nulo em testes que não exercitam referência
    /// entre políticas. Definido após a construção para evitar dependência
    /// circular na composição (o provider pode depender do executor).
    /// </summary>
    public ICompiledFlowProvider? PolicyProvider { get; set; }

    public FlowExecutor(IDataSourceResolver dataSources, ISourceCatalog sources, int maxSteps = 1000)
    {
        _dataSources = dataSources;
        _sources = sources;
        _maxSteps = maxSteps;
    }

    public async Task<DecisionResult> ExecuteAsync(
        CompiledFlow flow,
        DecisionRequest request,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<Guid>? policyStack = null,
        IPolicyByNameProvider? policyResolver = null)
    {
        // Resolvedor de subpolítica por nome desta decisão: quando a execução vem
        // de um bundle congelado, o DecisionService passa um resolvedor restrito
        // ao bundle (as subs vêm do retrato congelado, não da versão mais recente).
        // Sem ele, cai no provider singleton (fallback: políticas sem bundle).
        var policyByName = policyResolver ?? (PolicyProvider as IPolicyByNameProvider);

        var context = new DictionaryFormulaContext(request.Input);

        // Tabelas de parâmetros ficam inteiras no snapshot (congeladas), sem I/O;
        // semeamos todas no contexto para PROCV/PROCV.FAIXA consultarem direto.
        foreach (var table in flow.Snapshot.Tables)
        {
            context.SetTable(table);
        }

        var trace = new List<TraceStep>();
        var sequence = 0;

        // Pilha de políticas em execução (detecção de ciclo em (Política;...)).
        var stack = policyStack is null
            ? new HashSet<Guid> { flow.Snapshot.FlowId }
            : new HashSet<Guid>(policyStack) { flow.Snapshot.FlowId };
        // Cache por decisão: cada política referenciada é executada uma única vez.
        var policyResults = new Dictionary<Guid, DecisionResult>();
        // Variáveis avaliadas nesta execução (expostas no resultado p/ (Pol;Variaveis;x)).
        var evaluatedVariables = new Dictionary<string, Formulas.FormulaValue>(StringComparer.OrdinalIgnoreCase);

        // Resolução SOB DEMANDA (lazy): fontes e variáveis NÃO são resolvidas no
        // início. São resolvidas apenas quando uma fórmula efetivamente executada
        // as referencia — assim, se o fluxo recusa numa regra inicial, as fontes
        // das regras seguintes nunca são consultadas (não se paga por elas).
        var variablesByName = new Dictionary<string, CompiledVariable>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in flow.OrderedVariables) variablesByName[v.Name] = v;

        var resolvedExternals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Produtos (fonte+produto) buscados ONLINE nesta decisão — para marcar a
        // origem correta no relatório das fontes (ver loop de fontes abaixo).
        var onlineProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Garante que as fontes e variáveis que ESTA fórmula usa estejam no
        // contexto, resolvendo recursivamente (variável pode depender de fontes e
        // de outras variáveis). Cada fonte/variável é resolvida no máximo uma vez
        // por decisão. Chamada imediatamente antes de avaliar cada fórmula do nó.
        async Task EnsureResolvedAsync(Formulas.CompiledFormula formula)
        {
            // Variáveis referenciadas (recursivo: resolve dependências primeiro).
            foreach (var varName in formula.ReferencedVariables)
            {
                if (resolvedVariables.Contains(varName)) continue;
                if (!variablesByName.TryGetValue(varName, out var variable)) continue;
                resolvedVariables.Add(varName); // marca antes para evitar recursão em ciclo (já barrado na compilação)
                await EnsureResolvedAsync(variable.Formula);
                var (value, steps) = variable.Formula.EvaluateTraced(context);
                context.SetVariable(variable.Name, value);
                evaluatedVariables[variable.Name] = value;
                trace.Add(new TraceStep(sequence++, "(variável)", variable.Name,
                    null, $"{{{variable.Name}}} = {value}", "Variável calculada")
                { Category = TraceCategory.Variavel, Detail = steps });
            }

            // Fontes referenciadas ([Fonte;Produto;Dado]).
            foreach (var reference in formula.ExternalReferences)
            {
                var key = $"{reference.Source}\u0001{reference.Product}\u0001{reference.Datum}";
                if (!resolvedExternals.Add(key)) continue;
                var resolution = await _sources.ResolveWithOriginAsync(reference, context, cancellationToken);
                context.SetExternal(reference.Source, reference.Product, reference.Datum, resolution.Value);

                // Origem no relatório: o produto é buscado UMA vez por decisão.
                // Se JÁ buscamos este (fonte+produto) ao vivo agora, os demais
                // dados do mesmo produto também são "Online" (mesmo snapshot),
                // não "Cache" — mesmo que o 2º dado tecnicamente leia do cache
                // recém-gravado. Rastreamos os produtos buscados online nesta
                // decisão para refletir a semântica do relatório.
                var productKey = $"{reference.Source}\u0001{reference.Product}";
                var origin = resolution.Origin;
                if (origin == Sources.SourceOrigin.Online)
                {
                    onlineProducts.Add(productKey);
                }
                else if (onlineProducts.Contains(productKey))
                {
                    origin = Sources.SourceOrigin.Online;
                }

                trace.Add(new TraceStep(sequence++, "(fonte)", reference.Source,
                    reference.ToString(), resolution.Value.ToString(),
                    $"Consulta à fonte {reference}")
                { Category = TraceCategory.Fonte, SourceOrigin = origin.ToString() });
            }

            // Referências a outras políticas ((Política;Categoria;Variável)):
            // executa a política alvo sob demanda e semeia o valor no contexto.
            foreach (var pref in formula.PolicyReferences)
            {
                var value = await ResolvePolicyRefAsync(pref);
                context.SetPolicy(pref.Policy, pref.Category, pref.Variable, value);
                trace.Add(new TraceStep(sequence++, "(política)", pref.Policy,
                    pref.ToString(), value.ToString(),
                    $"Referência a política {pref}")
                { Category = TraceCategory.Fonte });
            }
        }

        // Executa a política referenciada (uma vez por decisão), detecta ciclo,
        // e extrai o valor pedido (Pontos/Limite/Resposta/Variável).
        async Task<FormulaValue> ResolvePolicyRefAsync(Sources.PolicyRef pref)
        {
            if (policyByName is null)
            {
                return FormulaValue.Error(FormulaErrorKind.NotAvailable);
            }

            // Resolve o alvo pelo nome (case-insensitive) via o provider? O provider
            // é por id; então usamos o nome comparando com o snapshot. Para manter
            // simples e robusto, o alvo é resolvido por nome no provider estendido.
            var targetFlow = await ResolveTargetByNameAsync(pref.Policy);
            if (targetFlow is null)
            {
                return FormulaValue.Error(FormulaErrorKind.Name); // política não encontrada/publicada
            }

            // Ciclo: A → B → A.
            if (stack.Contains(targetFlow.Snapshot.FlowId))
            {
                return FormulaValue.Error(FormulaErrorKind.NotAvailable);
            }

            // Cache por decisão.
            if (!policyResults.TryGetValue(targetFlow.Snapshot.FlowId, out var sub))
            {
                var subRequest = new DecisionRequest(targetFlow.Snapshot.FlowId, request.ProposalReference, request.Input);
                // Propaga o mesmo resolvedor do bundle nas subpolíticas (cascata).
                sub = await ExecuteAsync(targetFlow, subRequest, cancellationToken, stack, policyResolver);
                policyResults[targetFlow.Snapshot.FlowId] = sub;

                // Incorpora a trilha da subpolítica na trilha desta execução, para
                // a auditoria mostrar TUDO que rodou em todas as políticas. Só na
                // 1ª execução da sub (o cache evita duplicar). Cada passo da sub é
                // reindexado na sequência atual e marcado com o nome da política a
                // que pertence (PolicyName) — sem prefixar o rótulo do nó. Passos
                // já marcados (netos, de subpolíticas mais profundas) preservam o
                // nome original.
                var policyName = targetFlow.Snapshot.FlowName;
                foreach (var step in sub.Trace)
                {
                    trace.Add(step with
                    {
                        Sequence = sequence++,
                        PolicyName = step.PolicyName ?? policyName,
                    });
                }
            }

            var cat = pref.Category.Trim().ToLowerInvariant();
            return cat switch
            {
                "pontos" => FormulaValue.Number(sub.Score),
                "limite" => FormulaValue.Number(sub.Limit),
                "resposta" => FormulaValue.Text(sub.Resposta),
                "variaveis" or "variáveis" => sub.Variables.TryGetValue(pref.Variable, out var vv)
                    ? vv
                    : FormulaValue.Error(FormulaErrorKind.NotAvailable),
                _ => FormulaValue.Error(FormulaErrorKind.Name),
            };
        }

        // Resolve a política alvo pelo nome. Percorre os flows publicados via o
        // provider (que expõe por id); aqui usamos o nome do snapshot.
        async Task<CompiledFlow?> ResolveTargetByNameAsync(string name)
        {
            if (policyByName is null)
            {
                return null;
            }
            return await policyByName.GetByNameAsync(name, cancellationToken);
        }

        decimal score = 0m;
        decimal limit = 0m;
        var resposta = string.Empty;
        var justifications = new List<string>();
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Exposes the accumulated counters/state as the fields 'pontos', 'limite'
        // and 'resposta' so formulas/actions can read them mid-run (like Crivo's
        // [Criterio Atual;...]). 'resposta' is a free string a rule can set and a
        // later rule can test (e.g. 'resposta' = "NOK").
        void SyncCounters()
        {
            context.Set("pontos", FormulaValue.Number(score));
            context.Set("limite", FormulaValue.Number(limit));
            context.Set("resposta", FormulaValue.Text(resposta));
        }
        SyncCounters();

        // Antes de devolver, marca com o nome DESTA política os passos que ainda
        // não têm PolicyName (os passos próprios; os de subpolíticas já vêm
        // marcados do merge acima). Assim a trilha agrupa por política sem
        // depender de prefixos no rótulo do nó.
        List<TraceStep> StampPolicy()
        {
            var name = flow.Snapshot.FlowName;
            for (var i = 0; i < trace.Count; i++)
            {
                if (trace[i].PolicyName is null)
                {
                    trace[i] = trace[i] with { PolicyName = name };
                }
            }
            return trace;
        }

        // Helpers de resultado como funções locais: capturam os acumuladores
        // (incluindo resposta e as variáveis avaliadas) para expô-los no resultado.
        DecisionResult Completed(DecisionOutcome outcome)
            => new(flow.Snapshot.FlowId, flow.Snapshot.FlowVersionId, outcome, score, ExecutionStatus.Completed, null, StampPolicy())
            { Limit = limit, Justifications = justifications, Outputs = outputs, Resposta = resposta, Variables = evaluatedVariables };

        DecisionResult ManualReview()
            => new(flow.Snapshot.FlowId, flow.Snapshot.FlowVersionId, DecisionOutcome.ManualReview, score, ExecutionStatus.Completed, null, StampPolicy())
            { Limit = limit, Justifications = justifications, Outputs = outputs, Resposta = resposta, Variables = evaluatedVariables };

        DecisionResult Failed(string error)
            => new(flow.Snapshot.FlowId, flow.Snapshot.FlowVersionId, DecisionOutcome.Pending, score, ExecutionStatus.Failed, error, StampPolicy())
            { Limit = limit, Justifications = justifications, Outputs = outputs, Resposta = resposta, Variables = evaluatedVariables };

        // Aplica uma lista de ações (pontos/limite/justificativa/resposta/saída) de
        // um nó, atualizando os acumuladores e registrando cada uma na trilha.
        // Retorna true se alguma ação resultou em erro (o chamador deve encerrar
        // em revisão manual). Reaproveitada pelo nó Action e por qualquer nó com
        // ações anexadas (Condition por ramo, Decision, Computation, DataSource).
        async Task<bool> ApplyActions(PublishedNode node, IReadOnlyList<CompiledAction> actions)
        {
            foreach (var action in actions)
            {
                await EnsureResolvedAsync(action.Formula);
                var (value, steps) = action.Formula.EvaluateTraced(context);
                if (value.IsError)
                {
                    trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                        null, value.ToString(), "Erro em ação → revisão manual.")
                    { Category = TraceCategory.Acao, Detail = steps });
                    return true;
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
                    case ActionType.SetResposta:
                        resposta = value.AsText();
                        trace.Add(Step(ref sequence, node, $"resposta = \"{resposta}\"", "Define resposta", TraceCategory.Acao, steps));
                        break;
                }
                SyncCounters();
            }
            return false;
        }

        var currentKey = flow.StartNodeKey;
        var visitedSteps = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++visitedSteps > _maxSteps)
            {
                return Failed("Limite de passos excedido; possível ciclo no fluxo.");
            }

            if (!flow.NodesByKey.TryGetValue(currentKey, out var node))
            {
                return Failed($"Nó '{currentKey}' não encontrado no fluxo.");
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
                        await EnsureResolvedAsync(a.Formula);
                        var (value, steps) = a.Formula.EvaluateTraced(context);
                        if (value.IsError)
                        {
                            trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                                null, value.ToString(),
                                $"Erro ao calcular '{a.TargetField}' → revisão manual.")
                            { Detail = steps });
                            return ManualReview();
                        }
                        context.Set(a.TargetField, value);
                        trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                            null, $"{a.TargetField} = {value}", "Cálculo")
                        { Detail = steps });
                    }
                    // Ações anexadas ao nó de cálculo.
                    if (flow.NodeActionsByNode.TryGetValue(node.NodeKey, out var compActions)
                        && await ApplyActions(node, compActions))
                    {
                        return ManualReview();
                    }
                    break;
                }

                case FlowNodeKind.Action:
                {
                    if (await ApplyActions(node, flow.ActionsByNode[node.NodeKey]))
                    {
                        return ManualReview();
                    }
                    break;
                }

                case FlowNodeKind.Matrix:
                {
                    var matrix = flow.MatrixByNode[node.NodeKey];
                    await EnsureResolvedAsync(matrix.RowFormula);
                    await EnsureResolvedAsync(matrix.ColFormula);
                    var (rowVal, rowSteps) = matrix.RowFormula.EvaluateTraced(context);
                    var (colVal, colSteps) = matrix.ColFormula.EvaluateTraced(context);
                    var matrixSteps = new List<EvalStep>(rowSteps.Count + colSteps.Count);
                    matrixSteps.AddRange(rowSteps);
                    matrixSteps.AddRange(colSteps);
                    if (rowVal.IsError || colVal.IsError)
                    {
                        trace.Add(Step(ref sequence, node, "erro", "Erro na expressão da matriz → revisão manual.", TraceCategory.Matriz, matrixSteps));
                        return ManualReview();
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
                        return ManualReview();
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
                            return Completed(outcome);
                    }
                    SyncCounters();
                    break;
                }

                case FlowNodeKind.Condition:
                {
                    var formula = flow.ConditionByNode[node.NodeKey];
                    await EnsureResolvedAsync(formula);
                    var (result, condSteps) = formula.EvaluateTraced(context);
                    if (result.IsError)
                    {
                        trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                            null, result.ToString(), "Erro na condição → revisão manual.")
                        { Category = TraceCategory.Regra, Detail = condSteps });
                        return ManualReview();
                    }

                    var boolResult = FormulaCoercion.ToBoolean(result);
                    if (boolResult.IsError)
                    {
                        trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                            null, result.ToString(), "Condição não booleana → revisão manual.")
                        { Category = TraceCategory.Regra, Detail = condSteps });
                        return ManualReview();
                    }

                    var branch = boolResult.AsBoolean();
                    trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                        null, branch ? "VERDADEIRO" : "FALSO", $"Desvio: {(branch ? "true" : "false")}")
                    { Category = TraceCategory.Regra, Detail = condSteps });

                    // Ações do ramo escolhido (Crivo: "SE condição ENTÃO ações").
                    if (flow.ConditionActionsByNode.TryGetValue(node.NodeKey, out var branchActions))
                    {
                        var chosen = branch ? branchActions.TrueActions : branchActions.FalseActions;
                        if (await ApplyActions(node, chosen))
                        {
                            return ManualReview();
                        }
                    }

                    var next = FindNext(flow, node.NodeKey, branch ? "true" : "false");
                    if (next is null)
                    {
                        return Failed($"Condição '{node.Label}' sem aresta de saída para '{(branch ? "true" : "false")}'.");
                    }
                    currentKey = next;
                    continue;
                }

                case FlowNodeKind.Ruleset:
                {
                    if (node.RulesetId is null || !flow.RulesByRulesetId.TryGetValue(node.RulesetId.Value, out var rules))
                    {
                        return Failed($"Nó de regras '{node.Label}' sem conjunto de regras associado.");
                    }

                    foreach (var compiled in rules)
                    {
                        var rule = compiled.Rule;
                        await EnsureResolvedAsync(compiled.Condition);
                        var matched = compiled.Condition.Evaluate(context);
                        if (matched.IsError)
                        {
                            trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                                null, matched.ToString(),
                                $"Erro na regra '{rule.Name}' → revisão manual."));
                            return ManualReview();
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
                                return Completed(forced);

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
                    // Ações anexadas ao nó de fonte de dados.
                    if (flow.NodeActionsByNode.TryGetValue(node.NodeKey, out var dsActions)
                        && await ApplyActions(node, dsActions))
                    {
                        return ManualReview();
                    }
                    break;
                }

                case FlowNodeKind.Decision:
                {
                    var cfg = NodeConfig.Deserialize<DecisionConfig>(node.Config);
                    // Ações anexadas ao nó de decisão (aplicadas antes de encerrar).
                    if (flow.NodeActionsByNode.TryGetValue(node.NodeKey, out var decActions)
                        && await ApplyActions(node, decActions))
                    {
                        return ManualReview();
                    }
                    trace.Add(new TraceStep(sequence++, node.NodeKey, node.Label,
                        null, cfg.Outcome.ToString(), cfg.Message ?? "Desfecho final"));
                    return Completed(cfg.Outcome);
                }
            }

            // For sequential (single-output) nodes, follow the unlabelled edge.
            var following = FindNext(flow, currentKey, sourceHandle: null);
            if (following is null)
            {
                return Failed($"Nó '{node.Label}' sem próximo nó (aresta de saída ausente).");
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

}
