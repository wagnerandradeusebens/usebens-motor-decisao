using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.Formulas.Parsing;
using Xunit;

namespace MotorDecisao.Application.Tests.Formulas;

/// <summary>
/// Tests the deep, step-by-step resolution trace produced by <see cref="TracingEvaluator"/>
/// (via <see cref="CompiledFormula.EvaluateTraced"/>). The steps must be post-order
/// (children before parent), depth must mirror AST nesting, and short-circuit
/// functions must only record the branches actually evaluated.
/// </summary>
public class TracingEvaluatorTests
{
    private static (FormulaValue Value, IReadOnlyList<EvalStep> Steps) Trace(
        string expr, IFormulaContext ctx)
        => FormulaEngine.Compile(expr).EvaluateTraced(ctx);

    [Fact]
    public void Nested_arithmetic_produces_post_order_steps_with_depths()
    {
        var ctx = new DictionaryFormulaContext().Set("renda", FormulaValue.Number(1000));

        var (value, steps) = Trace("'renda' * 0.8 + 100", ctx);

        Assert.Equal(900m, value.AsNumber());

        // Post-order: children before parent; depth mirrors nesting.
        // AST: (('renda' * 0.8) + 100)
        Assert.Collection(steps,
            s => AssertStep(s, 2, "'renda'", "1000"),
            s => AssertStep(s, 2, "0.8", "0.8"),
            s => AssertStep(s, 1, "'renda' * 0.8", "800.0"),
            s => AssertStep(s, 1, "100", "100"),
            s => AssertStep(s, 0, "'renda' * 0.8 + 100", "900.0"));
    }

    [Fact]
    public void SE_traces_only_the_taken_branch()
    {
        var ctx = new DictionaryFormulaContext().Set("idade", FormulaValue.Number(20));

        var (value, steps) = Trace("SE('idade' >= 18; \"maior\"; \"menor\")", ctx);

        Assert.Equal("maior", value.AsText());

        // The condition and the taken branch appear; the "menor" branch must not.
        Assert.Contains(steps, s => s.Expression == "\"maior\"");
        Assert.DoesNotContain(steps, s => s.Expression == "\"menor\"");
        // The condition itself was resolved.
        Assert.Contains(steps, s => s.Expression == "'idade' >= 18");
    }

    [Fact]
    public void SE_else_branch_is_traced_when_condition_is_false()
    {
        var ctx = new DictionaryFormulaContext().Set("idade", FormulaValue.Number(15));

        var (value, steps) = Trace("SE('idade' >= 18; \"maior\"; \"menor\")", ctx);

        Assert.Equal("menor", value.AsText());
        Assert.Contains(steps, s => s.Expression == "\"menor\"");
        Assert.DoesNotContain(steps, s => s.Expression == "\"maior\"");
    }

    [Fact]
    public void E_short_circuits_and_stops_at_first_false()
    {
        // Second argument is false, so the third must never be evaluated/recorded.
        var (value, steps) = Trace("E(VERDADEIRO; FALSO; 1 / 0 = 0)", new DictionaryFormulaContext());

        Assert.False(value.AsBoolean());
        // Only the two evaluated args plus the root E(...) are recorded — the
        // short-circuited third argument produces no step of its own.
        Assert.DoesNotContain(steps, s => s.Expression == "1 / 0 = 0");
        Assert.Equal(3, steps.Count);
    }

    [Fact]
    public void The_root_step_holds_the_final_value()
    {
        var (value, steps) = Trace("2 + 3 * 4", new DictionaryFormulaContext());

        Assert.Equal(14m, value.AsNumber());
        var root = Assert.Single(steps, s => s.Depth == 0);
        Assert.Equal("2 + 3 * 4", root.Expression);
        Assert.Equal("14", root.Value);
    }

    private static void AssertStep(EvalStep step, int depth, string expression, string value)
    {
        Assert.Equal(depth, step.Depth);
        Assert.Equal(expression, step.Expression);
        Assert.Equal(value, step.Value);
    }
}
