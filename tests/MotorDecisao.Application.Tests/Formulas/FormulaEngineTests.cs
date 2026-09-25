using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.Formulas.Parsing;
using Xunit;

namespace MotorDecisao.Application.Tests.Formulas;

public class FormulaEngineTests
{
    private static FormulaValue Eval(string expr, IFormulaContext? ctx = null)
        => FormulaEngine.Evaluate(expr, ctx ?? new DictionaryFormulaContext());

    // --- Arithmetic & precedence -----------------------------------------

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("2 ^ 3 ^ 2", 512)]   // right-associative
    [InlineData("-2 + 5", 3)]
    [InlineData("2 + 3 = 5", 1)]     // comparison yields boolean (true)
    public void Evaluates_arithmetic_and_precedence(string expr, decimal expected)
    {
        var result = Eval(expr);
        if (result.Type == FormulaValueType.Boolean)
        {
            Assert.Equal(expected != 0, result.AsBoolean());
        }
        else
        {
            Assert.Equal(FormulaValueType.Number, result.Type);
            Assert.Equal(expected, result.AsNumber());
        }
    }

    [Fact]
    public void Division_by_zero_yields_div0_error()
    {
        var r = Eval("1 / 0");
        Assert.True(r.IsError);
        Assert.Equal(FormulaErrorKind.DivByZero, r.ErrorKind);
        Assert.Equal("#DIV/0!", r.ToString());
    }

    // --- pt-BR separator + logical short-circuit --------------------------

    [Fact]
    public void SE_uses_semicolon_separator_and_picks_branch()
    {
        var ctx = new DictionaryFormulaContext().Set("idade", FormulaValue.Number(20));
        var r = Eval("SE(idade >= 18; \"maior\"; \"menor\")", ctx);
        Assert.Equal(FormulaValueType.Text, r.Type);
        Assert.Equal("maior", r.AsText());
    }

    [Fact]
    public void E_and_OU_evaluate_correctly()
    {
        Assert.True(Eval("E(VERDADEIRO; 1 = 1; 2 > 1)").AsBoolean());
        Assert.False(Eval("E(VERDADEIRO; FALSO)").AsBoolean());
        Assert.True(Eval("OU(FALSO; FALSO; 3 > 2)").AsBoolean());
        Assert.False(Eval("OU(FALSO; FALSO)").AsBoolean());
    }

    [Fact]
    public void SE_does_not_evaluate_untaken_branch_error()
    {
        // The else branch divides by zero, but the condition is true, so it must
        // never be evaluated -> no error.
        var r = Eval("SE(VERDADEIRO; 42; 1/0)");
        Assert.Equal(42m, r.AsNumber());
    }

    [Fact]
    public void SEERRO_catches_errors()
    {
        Assert.Equal(0m, Eval("SEERRO(1/0; 0)").AsNumber());
        Assert.Equal(5m, Eval("SEERRO(10/2; 0)").AsNumber());
    }

    // --- Error propagation ------------------------------------------------

    [Fact]
    public void Errors_propagate_through_operators()
    {
        var r = Eval("(1/0) + 5");
        Assert.True(r.IsError);
        Assert.Equal(FormulaErrorKind.DivByZero, r.ErrorKind);
    }

    [Fact]
    public void Unknown_function_yields_name_error()
    {
        var r = Eval("NAOEXISTE(1; 2)");
        Assert.True(r.IsError);
        Assert.Equal(FormulaErrorKind.Name, r.ErrorKind);
    }

    // --- Functions --------------------------------------------------------

    [Theory]
    [InlineData("ARRED(3.14159; 2)", 3.14)]
    [InlineData("ABS(-7)", 7)]
    [InlineData("MAXIMO(3; 9; 5)", 9)]
    [InlineData("MINIMO(3; 9; 5)", 3)]
    [InlineData("SOMA(1; 2; 3; 4)", 10)]
    [InlineData("RESTO(10; 3)", 1)]
    [InlineData("ARREDONDAR.PARA.CIMA(2.1; 0)", 3)]
    [InlineData("ARREDONDAR.PARA.BAIXO(2.9; 0)", 2)]
    public void Math_functions(string expr, decimal expected)
        => Assert.Equal(expected, Eval(expr).AsNumber());

    [Theory]
    [InlineData("MAIUSCULA(\"abc\")", "ABC")]
    [InlineData("MINUSCULA(\"ABC\")", "abc")]
    [InlineData("ARRUMAR(\"  oi  \")", "oi")]
    [InlineData("ESQUERDA(\"credito\"; 3)", "cre")]
    [InlineData("DIREITA(\"credito\"; 2)", "to")]
    [InlineData("CONCATENAR(\"a\"; \"b\"; \"c\")", "abc")]
    public void Text_functions(string expr, string expected)
        => Assert.Equal(expected, Eval(expr).AsText());

    [Fact]
    public void Concatenation_operator_works()
    {
        var ctx = new DictionaryFormulaContext().Set("nome", FormulaValue.Text("Ana"));
        Assert.Equal("Ola, Ana!", Eval("\"Ola, \" & nome & \"!\"", ctx).AsText());
    }

    [Fact]
    public void NUM_CARACT_counts_characters()
        => Assert.Equal(7m, Eval("NUM.CARACT(\"credito\")").AsNumber());

    // --- Fields -----------------------------------------------------------

    [Fact]
    public void Unknown_field_is_blank_not_error()
    {
        var r = Eval("EHBRANCO(campo_inexistente)");
        Assert.True(r.AsBoolean());
    }

    [Fact]
    public void Field_names_are_case_insensitive()
    {
        var ctx = new DictionaryFormulaContext().Set("Renda", FormulaValue.Number(5000));
        Assert.Equal(5000m, Eval("renda", ctx).AsNumber());
    }

    // --- Dates ------------------------------------------------------------

    [Fact]
    public void DATADIF_computes_age_in_years()
    {
        var ctx = new DictionaryFormulaContext()
            .Set("nascimento", FormulaValue.Date(new DateTime(2000, 6, 15)))
            .Set("hoje", FormulaValue.Date(new DateTime(2026, 6, 14)));
        // One day before the 26th birthday -> 25.
        Assert.Equal(25m, Eval("DATADIF(nascimento; hoje; \"Y\")", ctx).AsNumber());
    }

    // --- Realistic credit-policy formulas ---------------------------------

    [Fact]
    public void Credit_policy_debt_to_income_gate()
    {
        var ctx = new DictionaryFormulaContext()
            .Set("divida_mensal", FormulaValue.Number(1500))
            .Set("renda_mensal", FormulaValue.Number(5000));
        // Approve when debt-to-income <= 0.30.
        var r = Eval("SE(divida_mensal / renda_mensal <= 0.30; \"APROVAR\"; \"NEGAR\")", ctx);
        Assert.Equal("APROVAR", r.AsText());
    }

    [Fact]
    public void Credit_policy_composite_rule()
    {
        var ctx = new DictionaryFormulaContext()
            .Set("idade", FormulaValue.Number(30))
            .Set("renda", FormulaValue.Number(8000))
            .Set("score", FormulaValue.Number(720));
        var r = Eval("E(idade >= 18; renda >= 2000; score > 600)", ctx);
        Assert.True(r.AsBoolean());
    }

    // --- Compilation reuse + referenced fields ----------------------------

    [Fact]
    public void Compiled_formula_reports_referenced_fields_and_reevaluates()
    {
        var compiled = FormulaEngine.Compile("renda * 0.3 + bonus");
        Assert.Contains("renda", compiled.ReferencedFields);
        Assert.Contains("bonus", compiled.ReferencedFields);
        Assert.Equal(2, compiled.ReferencedFields.Count);

        var ctx1 = new DictionaryFormulaContext()
            .Set("renda", FormulaValue.Number(1000)).Set("bonus", FormulaValue.Number(50));
        Assert.Equal(350m, compiled.Evaluate(ctx1).AsNumber());

        var ctx2 = new DictionaryFormulaContext()
            .Set("renda", FormulaValue.Number(2000)).Set("bonus", FormulaValue.Number(100));
        Assert.Equal(700m, compiled.Evaluate(ctx2).AsNumber());
    }

    // --- Syntax errors ----------------------------------------------------

    [Theory]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("SE(1; 2")]
    [InlineData("\"sem fim")]
    public void Syntax_errors_throw_formula_exception(string expr)
    {
        Assert.Throws<FormulaException>(() => FormulaEngine.Compile(expr));
    }

    [Fact]
    public void TryCompile_returns_error_without_throwing()
    {
        var ok = FormulaEngine.TryCompile("1 + ", out var formula, out var error);
        Assert.False(ok);
        Assert.Null(formula);
        Assert.NotNull(error);
    }
}
