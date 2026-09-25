using MotorDecisao.Application.Execution;
using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.Formulas.Parsing;
using MotorDecisao.Application.PublishedFlows;
using MotorDecisao.Domain.Enums;
using Xunit;

namespace MotorDecisao.Application.Tests.Formulas;

public class RefSyntaxTests
{
    // --- Field references with single quotes ------------------------------

    [Fact]
    public void Single_quoted_reads_field()
    {
        var ctx = new DictionaryFormulaContext().Set("renda", FormulaValue.Number(5000));
        Assert.Equal(5000m, FormulaEngine.Evaluate("'renda'", ctx).AsNumber());
    }

    [Fact]
    public void Double_quoted_is_text_literal_not_field()
    {
        var ctx = new DictionaryFormulaContext().Set("APROVAR", FormulaValue.Number(1));
        // "APROVAR" must remain the literal text, not read the field named APROVAR.
        Assert.Equal("APROVAR", FormulaEngine.Evaluate("\"APROVAR\"", ctx).AsText());
    }

    [Fact]
    public void Field_and_text_coexist_in_one_formula()
    {
        var ctx = new DictionaryFormulaContext().Set("idade", FormulaValue.Number(20));
        var r = FormulaEngine.Evaluate("SE('idade' >= 18; \"APROVAR\"; \"NEGAR\")", ctx);
        Assert.Equal("APROVAR", r.AsText());
    }

    [Fact]
    public void Bare_identifier_still_reads_field_for_back_compat()
    {
        var ctx = new DictionaryFormulaContext().Set("renda", FormulaValue.Number(3000));
        Assert.Equal(3000m, FormulaEngine.Evaluate("renda", ctx).AsNumber());
    }

    // --- Variable references with braces ----------------------------------

    [Fact]
    public void Brace_reads_resolved_variable()
    {
        var ctx = new DictionaryFormulaContext().SetVariable("comprometimento", FormulaValue.Number(0.25m));
        Assert.Equal(0.25m, FormulaEngine.Evaluate("{comprometimento}", ctx).AsNumber());
    }

    [Fact]
    public void Compiled_formula_reports_referenced_variables()
    {
        var compiled = FormulaEngine.Compile("{a} + {b} * 2");
        Assert.Contains("a", compiled.ReferencedVariables);
        Assert.Contains("b", compiled.ReferencedVariables);
    }

    [Theory]
    [InlineData("'campo sem fim")]
    [InlineData("{variavel_sem_fim")]
    [InlineData("{}")]
    public void Malformed_refs_are_syntax_errors(string expr)
        => Assert.Throws<FormulaException>(() => FormulaEngine.Compile(expr));

    // --- Variables resolved in dependency order (var-in-var) --------------

    private static PublishedFlowSnapshot SnapshotWithVariables(params (string key, string expr)[] vars)
    {
        var nodes = new[]
        {
            new PublishedNode("start", FlowNodeKind.Start, "start", "{}", null),
            new PublishedNode("dec", FlowNodeKind.Decision, "dec", "{\"outcome\":\"Approved\"}", null),
        };
        var edges = new[] { new PublishedEdge("e", "start", "dec", null, null) };
        var formulas = vars.Select(v => new PublishedFormula(v.key, v.key, v.expr)).ToList();
        return new PublishedFlowSnapshot(
            Guid.NewGuid(), "t", Guid.NewGuid(), 1, DateTime.UtcNow,
            nodes, edges, Array.Empty<PublishedRuleset>(), formulas);
    }

    [Fact]
    public void Variable_referencing_variable_resolves_in_order()
    {
        // c depends on b, b depends on 'renda' field. Declaration order shuffled.
        var snap = SnapshotWithVariables(
            ("c", "{b} * 2"),
            ("b", "'renda' + 100"));

        var flow = CompiledFlow.Compile(snap);
        // Ordered so b comes before c.
        var names = flow.OrderedVariables.Select(v => v.Name).ToList();
        Assert.True(names.IndexOf("b") < names.IndexOf("c"));
    }

    [Fact]
    public void Cycle_between_variables_is_rejected_at_compile()
    {
        var snap = SnapshotWithVariables(
            ("a", "{b} + 1"),
            ("b", "{a} + 1"));

        var ex = Assert.Throws<FlowCompilationException>(() => CompiledFlow.Compile(snap));
        Assert.Contains("Ciclo", ex.Message);
    }

    [Fact]
    public void Reference_to_unknown_variable_is_rejected_at_compile()
    {
        var snap = SnapshotWithVariables(("a", "{naoexiste} + 1"));
        Assert.Throws<FlowCompilationException>(() => CompiledFlow.Compile(snap));
    }
}
