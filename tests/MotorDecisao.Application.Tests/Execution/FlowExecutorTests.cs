using MotorDecisao.Application.Execution;
using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.PublishedFlows;
using MotorDecisao.Domain.Enums;
using Xunit;

namespace MotorDecisao.Application.Tests.Execution;

/// <summary>Test catalog that resolves no external sources.</summary>
file sealed class EmptySourceCatalog : MotorDecisao.Application.Sources.ISourceCatalog
{
    public IReadOnlyList<MotorDecisao.Application.Sources.SourceDescriptor> List() =>
        System.Array.Empty<MotorDecisao.Application.Sources.SourceDescriptor>();

    public Task<FormulaValue> ResolveAsync(
        MotorDecisao.Application.Sources.ExternalRef reference,
        MotorDecisao.Application.Formulas.IFormulaContext context,
        System.Threading.CancellationToken cancellationToken = default) =>
        Task.FromResult(FormulaValue.Error(MotorDecisao.Application.Formulas.FormulaErrorKind.NotAvailable));
}

public class FlowExecutorTests
{
    private static readonly FlowExecutor Executor = new(new NoOpDataSourceResolver(), new EmptySourceCatalog());

    // --- Snapshot builders -------------------------------------------------

    private static PublishedNode Node(string key, FlowNodeKind kind, string config = "{}", Guid? rulesetId = null)
        => new(key, kind, key, config, rulesetId);

    private static PublishedEdge Edge(string from, string to, string? handle = null)
        => new($"{from}->{to}", from, to, handle, null);

    private static PublishedFlowSnapshot Snapshot(
        IReadOnlyList<PublishedNode> nodes,
        IReadOnlyList<PublishedEdge> edges,
        IReadOnlyList<PublishedRuleset>? rulesets = null,
        string flowName = "test")
        => new(
            FlowId: Guid.NewGuid(),
            FlowName: flowName,
            FlowVersionId: Guid.NewGuid(),
            VersionNumber: 1,
            PublishedAt: DateTime.UtcNow,
            Nodes: nodes,
            Edges: edges,
            Rulesets: rulesets ?? Array.Empty<PublishedRuleset>(),
            Formulas: Array.Empty<PublishedFormula>());

    private static DecisionRequest Request(PublishedFlowSnapshot snap, params (string, FormulaValue)[] input)
    {
        var dict = new Dictionary<string, FormulaValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in input) dict[k] = v;
        return new DecisionRequest(snap.FlowId, "PROP-1", dict);
    }

    // --- Tests -------------------------------------------------------------

    [Fact]
    public async Task Condition_branches_true_to_approval()
    {
        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("cond", FlowNodeKind.Condition, "{\"expression\":\"idade >= 18\"}"),
                Node("approve", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}"),
                Node("deny", FlowNodeKind.Decision, "{\"outcome\":\"Denied\"}")
            },
            edges: new[]
            {
                Edge("start", "cond"),
                Edge("cond", "approve", "true"),
                Edge("cond", "deny", "false")
            });

        var flow = CompiledFlow.Compile(snap);
        var result = await Executor.ExecuteAsync(flow, Request(snap, ("idade", FormulaValue.Number(25))));

        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.Equal(DecisionOutcome.Approved, result.Outcome);
    }

    [Fact]
    public async Task Condition_branches_false_to_denial()
    {
        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("cond", FlowNodeKind.Condition, "{\"expression\":\"idade >= 18\"}"),
                Node("approve", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}"),
                Node("deny", FlowNodeKind.Decision, "{\"outcome\":\"Denied\"}")
            },
            edges: new[]
            {
                Edge("start", "cond"),
                Edge("cond", "approve", "true"),
                Edge("cond", "deny", "false")
            });

        var flow = CompiledFlow.Compile(snap);
        var result = await Executor.ExecuteAsync(flow, Request(snap, ("idade", FormulaValue.Number(16))));

        Assert.Equal(DecisionOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task Computation_writes_field_used_downstream()
    {
        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("calc", FlowNodeKind.Computation,
                    "{\"assignments\":[{\"targetField\":\"comprometimento\",\"expression\":\"divida / renda\"}]}"),
                Node("cond", FlowNodeKind.Condition, "{\"expression\":\"comprometimento <= 0.30\"}"),
                Node("approve", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}"),
                Node("review", FlowNodeKind.Decision, "{\"outcome\":\"ManualReview\"}")
            },
            edges: new[]
            {
                Edge("start", "calc"),
                Edge("calc", "cond"),
                Edge("cond", "approve", "true"),
                Edge("cond", "review", "false")
            });

        var flow = CompiledFlow.Compile(snap);
        var result = await Executor.ExecuteAsync(flow,
            Request(snap, ("divida", FormulaValue.Number(1000)), ("renda", FormulaValue.Number(5000))));

        Assert.Equal(DecisionOutcome.Approved, result.Outcome);
        Assert.Contains(result.Trace, t => t.Result != null && t.Result.Contains("comprometimento"));
    }

    [Fact]
    public async Task Ruleset_accumulates_score_and_decision_rule_forces_outcome()
    {
        var rulesetId = Guid.NewGuid();
        var rules = new PublishedRuleset(
            rulesetId, "scorecard", ApprovalThreshold: 50m,
            Rules: new[]
            {
                new PublishedRule(Guid.NewGuid(), 1, "bom score", "score > 600",
                    RuleEffect.Score, 30m, null, "faixa boa"),
                new PublishedRule(Guid.NewGuid(), 2, "renda alta", "renda >= 5000",
                    RuleEffect.Score, 25m, null, "renda ok"),
                new PublishedRule(Guid.NewGuid(), 3, "restricao", "tem_restricao = VERDADEIRO",
                    RuleEffect.Decision, 0m, DecisionOutcome.Denied, "possui restrição")
            });

        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("rs", FlowNodeKind.Ruleset, "{}", rulesetId),
                Node("approve", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}")
            },
            edges: new[]
            {
                Edge("start", "rs"),
                Edge("rs", "approve")
            },
            rulesets: new[] { rules });

        var flow = CompiledFlow.Compile(snap);

        // No restriction: rules score, flow proceeds to approval, score = 55.
        var ok = await Executor.ExecuteAsync(flow, Request(snap,
            ("score", FormulaValue.Number(700)),
            ("renda", FormulaValue.Number(6000)),
            ("tem_restricao", FormulaValue.Boolean(false))));
        Assert.Equal(DecisionOutcome.Approved, ok.Outcome);
        Assert.Equal(55m, ok.Score);

        // With restriction: decision rule forces Denied.
        var denied = await Executor.ExecuteAsync(flow, Request(snap,
            ("score", FormulaValue.Number(700)),
            ("renda", FormulaValue.Number(6000)),
            ("tem_restricao", FormulaValue.Boolean(true))));
        Assert.Equal(DecisionOutcome.Denied, denied.Outcome);
    }

    // --- Safety ------------------------------------------------------------

    [Fact]
    public async Task Runtime_formula_error_routes_to_manual_review()
    {
        // divida / renda with renda = 0 -> #DIV/0! at runtime -> ManualReview.
        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("calc", FlowNodeKind.Computation,
                    "{\"assignments\":[{\"targetField\":\"r\",\"expression\":\"divida / renda\"}]}"),
                Node("approve", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}")
            },
            edges: new[] { Edge("start", "calc"), Edge("calc", "approve") });

        var flow = CompiledFlow.Compile(snap);
        var result = await Executor.ExecuteAsync(flow,
            Request(snap, ("divida", FormulaValue.Number(100)), ("renda", FormulaValue.Number(0))));

        Assert.Equal(DecisionOutcome.ManualReview, result.Outcome);
        Assert.Contains(result.Trace, t => t.Message != null && t.Message.Contains("revisão manual"));
    }

    [Fact]
    public async Task Missing_outgoing_edge_is_controlled_failure()
    {
        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("calc", FlowNodeKind.Computation,
                    "{\"assignments\":[{\"targetField\":\"x\",\"expression\":\"1 + 1\"}]}")
            },
            edges: new[] { Edge("start", "calc") }); // calc has no outgoing edge

        var flow = CompiledFlow.Compile(snap);
        var result = await Executor.ExecuteAsync(flow, Request(snap));

        Assert.Equal(ExecutionStatus.Failed, result.Status);
        Assert.Equal(DecisionOutcome.Pending, result.Outcome);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Cyclic_graph_hits_step_limit()
    {
        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("a", FlowNodeKind.Computation, "{\"assignments\":[]}"),
                Node("b", FlowNodeKind.Computation, "{\"assignments\":[]}")
            },
            edges: new[] { Edge("start", "a"), Edge("a", "b"), Edge("b", "a") }); // a<->b loop

        var flow = CompiledFlow.Compile(snap);
        var executor = new FlowExecutor(new NoOpDataSourceResolver(), new EmptySourceCatalog(), maxSteps: 50);
        var result = await executor.ExecuteAsync(flow, Request(snap));

        Assert.Equal(ExecutionStatus.Failed, result.Status);
        Assert.Contains("ciclo", result.Error);
    }

    // --- Actions (points/limit/justification/outputs) --------------------

    [Fact]
    public async Task Action_node_accumulates_points_limit_justification_output()
    {
        var actionsCfg = "{\"actions\":[" +
            "{\"type\":\"AddPoints\",\"expression\":\"30\"}," +
            "{\"type\":\"AddLimit\",\"expression\":\"'renda' * 3\"}," +
            "{\"type\":\"AddJustification\",\"expression\":\"\\\"cliente ok\\\"\"}," +
            "{\"type\":\"SetOutput\",\"name\":\"taxa\",\"expression\":\"1.99\"}" +
            "]}";

        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("act", FlowNodeKind.Action, actionsCfg),
                Node("ap", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}")
            },
            edges: new[] { Edge("start", "act"), Edge("act", "ap") });

        var flow = CompiledFlow.Compile(snap);
        var result = await Executor.ExecuteAsync(flow, Request(snap, ("renda", FormulaValue.Number(1000))));

        Assert.Equal(DecisionOutcome.Approved, result.Outcome);
        Assert.Equal(30m, result.Score);
        Assert.Equal(3000m, result.Limit);
        Assert.Contains("cliente ok", result.Justifications);
        Assert.Equal("1.99", result.Outputs["taxa"]);
    }

    [Fact]
    public async Task SetJustification_clears_previous_ones()
    {
        var cfg = "{\"actions\":[" +
            "{\"type\":\"AddJustification\",\"expression\":\"\\\"primeira\\\"\"}," +
            "{\"type\":\"SetJustification\",\"expression\":\"\\\"final\\\"\"}" +
            "]}";
        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("act", FlowNodeKind.Action, cfg),
                Node("ap", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}")
            },
            edges: new[] { Edge("start", "act"), Edge("act", "ap") });

        var result = await Executor.ExecuteAsync(CompiledFlow.Compile(snap), Request(snap));
        Assert.Single(result.Justifications);
        Assert.Equal("final", result.Justifications[0]);
    }

    [Fact]
    public async Task Points_counter_readable_mid_run_via_field()
    {
        // First action adds 40; a later condition reads accumulated 'pontos'.
        var cfg = "{\"actions\":[{\"type\":\"AddPoints\",\"expression\":\"40\"}]}";
        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("act", FlowNodeKind.Action, cfg),
                Node("cond", FlowNodeKind.Condition, "{\"expression\":\"'pontos' >= 40\"}"),
                Node("ap", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}"),
                Node("ng", FlowNodeKind.Decision, "{\"outcome\":\"Denied\"}")
            },
            edges: new[]
            {
                Edge("start", "act"), Edge("act", "cond"),
                Edge("cond", "ap", "true"), Edge("cond", "ng", "false")
            });

        var result = await Executor.ExecuteAsync(CompiledFlow.Compile(snap), Request(snap));
        Assert.Equal(DecisionOutcome.Approved, result.Outcome);
    }

    // --- Ações anexadas a qualquer nó (não só ao nó Action) --------------

    [Fact]
    public async Task Condition_true_branch_actions_add_points()
    {
        // A condição pontua no ramo VERDADEIRO (Crivo: "SE ... ENTÃO ações").
        var condCfg = "{\"expression\":\"idade >= 18\"," +
            "\"trueActions\":[{\"type\":\"AddPoints\",\"expression\":\"30\"}]," +
            "\"falseActions\":[{\"type\":\"AddPoints\",\"expression\":\"5\"}]}";
        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("cond", FlowNodeKind.Condition, condCfg),
                Node("ap", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}"),
                Node("ng", FlowNodeKind.Decision, "{\"outcome\":\"Denied\"}")
            },
            edges: new[]
            {
                Edge("start", "cond"),
                Edge("cond", "ap", "true"),
                Edge("cond", "ng", "false")
            });
        var flow = CompiledFlow.Compile(snap);

        var maior = await Executor.ExecuteAsync(flow, Request(snap, ("idade", FormulaValue.Number(25))));
        Assert.Equal(DecisionOutcome.Approved, maior.Outcome);
        Assert.Equal(30m, maior.Score); // ramo verdadeiro

        var menor = await Executor.ExecuteAsync(flow, Request(snap, ("idade", FormulaValue.Number(15))));
        Assert.Equal(DecisionOutcome.Denied, menor.Outcome);
        Assert.Equal(5m, menor.Score); // ramo falso
    }

    [Fact]
    public async Task Decision_node_actions_apply_before_completing()
    {
        // A decisão define pontos e resposta antes de encerrar.
        var decCfg = "{\"outcome\":\"Approved\"," +
            "\"actions\":[" +
            "{\"type\":\"SetPoints\",\"expression\":\"90\"}," +
            "{\"type\":\"SetResposta\",\"expression\":\"\\\"OK\\\"\"}]}";
        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("ap", FlowNodeKind.Decision, decCfg)
            },
            edges: new[] { Edge("start", "ap") });

        var r = await Executor.ExecuteAsync(CompiledFlow.Compile(snap), Request(snap));
        Assert.Equal(DecisionOutcome.Approved, r.Outcome);
        Assert.Equal(90m, r.Score);
    }

    [Fact]
    public async Task Resposta_set_in_condition_readable_by_later_condition()
    {
        // Uma condição define resposta="NOK" no ramo verdadeiro; uma condição
        // posterior lê 'resposta' e nega.
        var c1 = "{\"expression\":\"tem_restricao = VERDADEIRO\"," +
            "\"trueActions\":[{\"type\":\"SetResposta\",\"expression\":\"\\\"NOK\\\"\"}]}";
        var c2 = "{\"expression\":\"'resposta' = \\\"NOK\\\"\"}";
        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("c1", FlowNodeKind.Condition, c1),
                Node("c2", FlowNodeKind.Condition, c2),
                Node("ng", FlowNodeKind.Decision, "{\"outcome\":\"Denied\"}"),
                Node("ap", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}")
            },
            edges: new[]
            {
                Edge("start", "c1"),
                // Em qualquer ramo de c1, segue para c2 (V e F apontam para c2).
                Edge("c1", "c2", "true"),
                Edge("c1", "c2", "false"),
                Edge("c2", "ng", "true"),
                Edge("c2", "ap", "false")
            });
        var flow = CompiledFlow.Compile(snap);

        var comRestricao = await Executor.ExecuteAsync(flow, Request(snap, ("tem_restricao", FormulaValue.Boolean(true))));
        Assert.Equal(DecisionOutcome.Denied, comRestricao.Outcome);

        var semRestricao = await Executor.ExecuteAsync(flow, Request(snap, ("tem_restricao", FormulaValue.Boolean(false))));
        Assert.Equal(DecisionOutcome.Approved, semRestricao.Outcome);
    }

    // --- External source lookups traced ----------------------------------

    [Fact]
    public async Task Lazy_source_not_consulted_when_its_branch_is_not_executed()
    {
        // A fonte só é usada num Cálculo APÓS uma condição. Se a condição manda
        // para Denied (ramo falso), o cálculo com a fonte nunca roda → a fonte
        // NÃO deve ser consultada (não se paga por ela).
        var catalog = new CountingCatalog(800m);
        var executor = new FlowExecutor(new NoOpDataSourceResolver(), catalog);

        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("cond", FlowNodeKind.Condition, "{\"expression\":\"idade >= 18\"}"),
                Node("calc", FlowNodeKind.Computation,
                    "{\"assignments\":[{\"targetField\":\"s\",\"expression\":\"[SERASA;Score;Pontuacao]\"}]}"),
                Node("ap", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}"),
                Node("ng", FlowNodeKind.Decision, "{\"outcome\":\"Denied\"}")
            },
            edges: new[]
            {
                Edge("start", "cond"),
                Edge("cond", "calc", "true"),
                Edge("calc", "ap"),
                Edge("cond", "ng", "false")
            });
        var flow = CompiledFlow.Compile(snap);

        // idade 16 → ramo falso → Denied; o cálculo com a fonte não executa.
        var negado = await executor.ExecuteAsync(flow, Request(snap, ("idade", FormulaValue.Number(16))));
        Assert.Equal(DecisionOutcome.Denied, negado.Outcome);
        Assert.Equal(0, catalog.Calls); // FONTE NÃO consultada

        // idade 25 → ramo verdadeiro → o cálculo roda e consulta a fonte 1 vez.
        var aprovado = await executor.ExecuteAsync(flow, Request(snap, ("idade", FormulaValue.Number(25))));
        Assert.Equal(DecisionOutcome.Approved, aprovado.Outcome);
        Assert.Equal(1, catalog.Calls);
    }

    /// <summary>Catálogo que conta quantas vezes uma fonte foi consultada.</summary>
    private sealed class CountingCatalog : MotorDecisao.Application.Sources.ISourceCatalog
    {
        private readonly decimal _value;
        public int Calls { get; private set; }
        public CountingCatalog(decimal value) => _value = value;
        public IReadOnlyList<MotorDecisao.Application.Sources.SourceDescriptor> List() =>
            System.Array.Empty<MotorDecisao.Application.Sources.SourceDescriptor>();
        public Task<FormulaValue> ResolveAsync(
            MotorDecisao.Application.Sources.ExternalRef reference,
            MotorDecisao.Application.Formulas.IFormulaContext context,
            System.Threading.CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(FormulaValue.Number(_value));
        }
    }

    // --- Referência cross-política ((Política;Categoria;Var)) ------------

    [Fact]
    public async Task Policy_reference_executes_target_and_uses_its_score()
    {
        // Política B: pontua 70 e aprova.
        var snapB = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("ap", FlowNodeKind.Decision, "{\"outcome\":\"Approved\",\"actions\":[{\"type\":\"SetPoints\",\"expression\":\"70\"}]}")
            },
            edges: new[] { Edge("start", "ap") },
            flowName: "POLITICA_B");
        var flowB = CompiledFlow.Compile(snapB);

        // Política A: usa os pontos de B — (POLITICA_B;Pontos) >= 50 → aprova.
        var snapA = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("cond", FlowNodeKind.Condition, "{\"expression\":\"(POLITICA_B;Pontos) >= 50\"}"),
                Node("ap", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}"),
                Node("ng", FlowNodeKind.Decision, "{\"outcome\":\"Denied\"}")
            },
            edges: new[]
            {
                Edge("start", "cond"),
                Edge("cond", "ap", "true"),
                Edge("cond", "ng", "false")
            },
            flowName: "POLITICA_A");
        var flowA = CompiledFlow.Compile(snapA);

        var executor = new FlowExecutor(new NoOpDataSourceResolver(), new EmptySourceCatalog())
        {
            PolicyProvider = new StubPolicyProvider(new() { ["POLITICA_B"] = flowB }),
        };

        var result = await executor.ExecuteAsync(flowA, Request(snapA));
        Assert.Equal(DecisionOutcome.Approved, result.Outcome);
    }

    [Fact]
    public async Task Policy_reference_reads_target_resposta()
    {
        // B define resposta = "NOK".
        var snapB = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("ap", FlowNodeKind.Decision, "{\"outcome\":\"Approved\",\"actions\":[{\"type\":\"SetResposta\",\"expression\":\"\\\"NOK\\\"\"}]}")
            },
            edges: new[] { Edge("start", "ap") },
            flowName: "BUREAU");
        var flowB = CompiledFlow.Compile(snapB);

        // A nega se a resposta de B for "NOK".
        var snapA = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("cond", FlowNodeKind.Condition, "{\"expression\":\"(BUREAU;Resposta) = \\\"NOK\\\"\"}"),
                Node("ng", FlowNodeKind.Decision, "{\"outcome\":\"Denied\"}"),
                Node("ap", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}")
            },
            edges: new[]
            {
                Edge("start", "cond"),
                Edge("cond", "ng", "true"),
                Edge("cond", "ap", "false")
            },
            flowName: "PRINCIPAL");
        var flowA = CompiledFlow.Compile(snapA);

        var executor = new FlowExecutor(new NoOpDataSourceResolver(), new EmptySourceCatalog())
        {
            PolicyProvider = new StubPolicyProvider(new() { ["BUREAU"] = flowB }),
        };

        var result = await executor.ExecuteAsync(flowA, Request(snapA));
        Assert.Equal(DecisionOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task Policy_reference_includes_subpolicy_trace_steps()
    {
        // Subpolítica BUREAU: um Start + Decision. A trilha dela terá passos
        // próprios (início, decisão) que devem aparecer na trilha da principal.
        var snapB = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("ap", FlowNodeKind.Decision, "{\"outcome\":\"Approved\",\"actions\":[{\"type\":\"SetResposta\",\"expression\":\"\\\"OK\\\"\"}]}")
            },
            edges: new[] { Edge("start", "ap") },
            flowName: "BUREAU");
        var flowB = CompiledFlow.Compile(snapB);

        var snapA = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("cond", FlowNodeKind.Condition, "{\"expression\":\"$[BUREAU;Resposta] = \\\"OK\\\"\"}"),
                Node("ap", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}"),
                Node("ng", FlowNodeKind.Decision, "{\"outcome\":\"Denied\"}")
            },
            edges: new[]
            {
                Edge("start", "cond"),
                Edge("cond", "ap", "true"),
                Edge("cond", "ng", "false")
            },
            flowName: "PRINCIPAL");
        var flowA = CompiledFlow.Compile(snapA);

        var executor = new FlowExecutor(new NoOpDataSourceResolver(), new EmptySourceCatalog())
        {
            PolicyProvider = new StubPolicyProvider(new() { ["BUREAU"] = flowB }),
        };

        var result = await executor.ExecuteAsync(flowA, Request(snapA));

        // A trilha da principal deve conter passos da subpolítica, marcados com o
        // nome dela em PolicyName (não mais prefixados no rótulo), e os passos
        // próprios devem carregar o nome da principal.
        Assert.Contains(result.Trace, s => s.PolicyName == "BUREAU");
        Assert.Contains(result.Trace, s => s.PolicyName == "PRINCIPAL");
        // A sequência é contínua e sem duplicatas (a sub roda uma vez).
        var seqs = result.Trace.Select(s => s.Sequence).ToList();
        Assert.Equal(seqs.Count, seqs.Distinct().Count());
    }

    /// <summary>Provider de política por nome, em memória, para os testes.</summary>
    private sealed class StubPolicyProvider : ICompiledFlowProvider, IPolicyByNameProvider
    {
        private readonly Dictionary<string, CompiledFlow> _byName;
        public StubPolicyProvider(Dictionary<string, CompiledFlow> byName) => _byName = byName;
        public Task<CompiledFlow?> GetByNameAsync(string policyName, CancellationToken ct = default)
            => Task.FromResult(_byName.TryGetValue(policyName, out var f) ? f : null);
        public Task<CompiledFlow?> GetAsync(Guid flowId, CancellationToken ct = default)
            => Task.FromResult<CompiledFlow?>(_byName.Values.FirstOrDefault(f => f.Snapshot.FlowId == flowId));
        public void Invalidate(Guid flowId) { }
        public void InvalidateAll() { }
    }

    [Fact]
    public async Task External_source_lookup_appears_in_trace()
    {
        // Catalog that resolves [SERASA;Score;Pontuacao] = 800.
        var catalog = new StubCatalog(800m);
        var executor = new FlowExecutor(new NoOpDataSourceResolver(), catalog);

        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("calc", FlowNodeKind.Computation,
                    "{\"assignments\":[{\"targetField\":\"s\",\"expression\":\"[SERASA;Score;Pontuacao]\"}]}"),
                Node("ap", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}")
            },
            edges: new[] { Edge("start", "calc"), Edge("calc", "ap") });

        var result = await executor.ExecuteAsync(CompiledFlow.Compile(snap), Request(snap));

        Assert.Contains(result.Trace, t => t.NodeKey == "(fonte)" && (t.Message ?? "").Contains("SERASA"));
    }

    private sealed class StubCatalog : MotorDecisao.Application.Sources.ISourceCatalog
    {
        private readonly decimal _value;
        public StubCatalog(decimal value) => _value = value;
        public IReadOnlyList<MotorDecisao.Application.Sources.SourceDescriptor> List() =>
            System.Array.Empty<MotorDecisao.Application.Sources.SourceDescriptor>();
        public Task<FormulaValue> ResolveAsync(
            MotorDecisao.Application.Sources.ExternalRef reference,
            MotorDecisao.Application.Formulas.IFormulaContext context,
            System.Threading.CancellationToken cancellationToken = default)
            => Task.FromResult(FormulaValue.Number(_value));
    }

    // --- Matrix ----------------------------------------------------------

    // renda (rows: <2000, 2000-5000, >=5000) × score (cols: <500, >=500) -> points.
    private const string MatrixPointsCfg =
        "{\"mode\":\"Points\",\"rowExpression\":\"'renda'\",\"colExpression\":\"'score'\"," +
        "\"rowBands\":[{\"label\":\"baixa\",\"min\":null,\"max\":2000},{\"label\":\"media\",\"min\":2000,\"max\":5000},{\"label\":\"alta\",\"min\":5000,\"max\":null}]," +
        "\"colBands\":[{\"label\":\"ruim\",\"min\":null,\"max\":500},{\"label\":\"bom\",\"min\":500,\"max\":null}]," +
        "\"cells\":[[\"0\",\"10\"],[\"10\",\"25\"],[\"20\",\"40\"]],\"defaultValue\":\"0\"}";

    private static PublishedFlowSnapshot MatrixSnapshot(string cfg)
    {
        return Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("m", FlowNodeKind.Matrix, cfg),
                Node("ap", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}")
            },
            edges: new[] { Edge("start", "m"), Edge("m", "ap") });
    }

    [Fact]
    public async Task Matrix_points_adds_correct_cell()
    {
        var snap = MatrixSnapshot(MatrixPointsCfg);
        var flow = CompiledFlow.Compile(snap);

        // renda 3000 (media) × score 700 (bom) -> cell [1][1] = 25.
        var r = await Executor.ExecuteAsync(flow, Request(snap,
            ("renda", FormulaValue.Number(3000)), ("score", FormulaValue.Number(700))));
        Assert.Equal(DecisionOutcome.Approved, r.Outcome);
        Assert.Equal(25m, r.Score);
    }

    [Fact]
    public async Task Matrix_selects_band_by_range()
    {
        var snap = MatrixSnapshot(MatrixPointsCfg);
        var flow = CompiledFlow.Compile(snap);

        // renda 500 (baixa) × score 300 (ruim) -> cell [0][0] = 0.
        var low = await Executor.ExecuteAsync(flow, Request(snap,
            ("renda", FormulaValue.Number(500)), ("score", FormulaValue.Number(300))));
        Assert.Equal(0m, low.Score);

        // renda 9000 (alta) × score 800 (bom) -> cell [2][1] = 40.
        var high = await Executor.ExecuteAsync(flow, Request(snap,
            ("renda", FormulaValue.Number(9000)), ("score", FormulaValue.Number(800))));
        Assert.Equal(40m, high.Score);
    }

    [Fact]
    public async Task Matrix_decision_mode_terminates_with_cell_outcome()
    {
        var cfg =
            "{\"mode\":\"Decision\",\"rowExpression\":\"'renda'\",\"colExpression\":\"'score'\"," +
            "\"rowBands\":[{\"label\":\"baixa\",\"min\":null,\"max\":3000},{\"label\":\"alta\",\"min\":3000,\"max\":null}]," +
            "\"colBands\":[{\"label\":\"ruim\",\"min\":null,\"max\":500},{\"label\":\"bom\",\"min\":500,\"max\":null}]," +
            "\"cells\":[[\"Denied\",\"ManualReview\"],[\"ManualReview\",\"Approved\"]],\"defaultValue\":\"ManualReview\"}";
        var snap = MatrixSnapshot(cfg);
        var flow = CompiledFlow.Compile(snap);

        var r = await Executor.ExecuteAsync(flow, Request(snap,
            ("renda", FormulaValue.Number(5000)), ("score", FormulaValue.Number(700))));
        Assert.Equal(DecisionOutcome.Approved, r.Outcome); // cell [1][1]
    }

    [Fact]
    public void Compilation_rejects_flow_without_start()
    {
        var snap = Snapshot(
            nodes: new[] { Node("decide", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}") },
            edges: Array.Empty<PublishedEdge>());

        Assert.Throws<FlowCompilationException>(() => CompiledFlow.Compile(snap));
    }

    [Fact]
    public void Compilation_rejects_invalid_formula()
    {
        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("cond", FlowNodeKind.Condition, "{\"expression\":\"idade >= \"}"),
                Node("approve", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}")
            },
            edges: new[] { Edge("start", "cond"), Edge("cond", "approve", "true") });

        Assert.Throws<FlowCompilationException>(() => CompiledFlow.Compile(snap));
    }

    // --- End-to-end --------------------------------------------------------

    [Fact]
    public async Task End_to_end_full_flow()
    {
        var rulesetId = Guid.NewGuid();
        var ruleset = new PublishedRuleset(
            rulesetId, "scorecard", 40m,
            new[]
            {
                new PublishedRule(Guid.NewGuid(), 1, "score alto", "score >= 700",
                    RuleEffect.Score, 40m, null, "score alto"),
                new PublishedRule(Guid.NewGuid(), 2, "cliente antigo", "meses_casa >= 12",
                    RuleEffect.Score, 20m, null, "cliente antigo")
            });

        // Start -> Computation(comprometimento) -> Condition(<=0.35) -> Ruleset -> Decision(Approved)
        //                                                     \-false-> Decision(Denied)
        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("calc", FlowNodeKind.Computation,
                    "{\"assignments\":[{\"targetField\":\"comprometimento\",\"expression\":\"ARRED(divida / renda; 2)\"}]}"),
                Node("cond", FlowNodeKind.Condition, "{\"expression\":\"comprometimento <= 0.35\"}"),
                Node("rs", FlowNodeKind.Ruleset, "{}", rulesetId),
                Node("approve", FlowNodeKind.Decision, "{\"outcome\":\"Approved\",\"message\":\"dentro da política\"}"),
                Node("deny", FlowNodeKind.Decision, "{\"outcome\":\"Denied\"}")
            },
            edges: new[]
            {
                Edge("start", "calc"),
                Edge("calc", "cond"),
                Edge("cond", "rs", "true"),
                Edge("cond", "deny", "false"),
                Edge("rs", "approve")
            },
            rulesets: new[] { ruleset });

        var flow = CompiledFlow.Compile(snap);
        var result = await Executor.ExecuteAsync(flow, Request(snap,
            ("divida", FormulaValue.Number(1500)),
            ("renda", FormulaValue.Number(6000)),
            ("score", FormulaValue.Number(720)),
            ("meses_casa", FormulaValue.Number(24))));

        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.Equal(DecisionOutcome.Approved, result.Outcome);
        Assert.Equal(60m, result.Score);                  // 40 + 20
        Assert.NotEmpty(result.Trace);
        Assert.Equal("start", result.Trace[0].NodeKey);   // trace starts at Start
    }

    [Fact]
    public async Task Disconnected_comment_node_does_not_affect_execution()
    {
        // A Comment sitting on the canvas but not wired into the path must not be
        // visited nor change the outcome.
        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("note", FlowNodeKind.Comment, "{\"text\":\"apenas uma anotação\"}"),
                Node("approve", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}")
            },
            edges: new[] { Edge("start", "approve") });

        var flow = CompiledFlow.Compile(snap);
        var result = await Executor.ExecuteAsync(flow, Request(snap));

        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.Equal(DecisionOutcome.Approved, result.Outcome);
        Assert.DoesNotContain(result.Trace, t => t.NodeKey == "note");
    }

    [Fact]
    public async Task Comment_node_on_path_passes_through_without_effect()
    {
        // If a Comment ends up wired between two nodes, it simply passes control
        // along to its outgoing edge.
        var snap = Snapshot(
            nodes: new[]
            {
                Node("start", FlowNodeKind.Start),
                Node("note", FlowNodeKind.Comment, "{\"text\":\"no meio do caminho\"}"),
                Node("approve", FlowNodeKind.Decision, "{\"outcome\":\"Approved\"}")
            },
            edges: new[] { Edge("start", "note"), Edge("note", "approve") });

        var flow = CompiledFlow.Compile(snap);
        var result = await Executor.ExecuteAsync(flow, Request(snap));

        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.Equal(DecisionOutcome.Approved, result.Outcome);
    }
}
