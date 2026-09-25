using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.Formulas.Parsing;
using Xunit;

namespace MotorDecisao.Application.Tests.Formulas;

/// <summary>
/// Tests for RAIZ (square root), RAIZCUBICA (cube root) and TRUNCAR (number/text).
/// </summary>
public class MathTextFunctionsTests
{
    private static FormulaValue Eval(string expr, IFormulaContext? ctx = null)
        => FormulaEngine.Evaluate(expr, ctx ?? new DictionaryFormulaContext());

    // --- RAIZ -------------------------------------------------------------

    [Theory]
    [InlineData("RAIZ(9)", 3)]
    [InlineData("RAIZ(2)", 1.4142135623730951)]
    [InlineData("RAIZ(0)", 0)]
    public void Raiz_computes_square_root(string expr, double expected)
    {
        var r = Eval(expr);
        Assert.Equal(FormulaValueType.Number, r.Type);
        Assert.Equal((decimal)expected, r.AsNumber(), 6);
    }

    [Fact]
    public void Raiz_of_negative_is_num_error()
    {
        var r = Eval("RAIZ(-4)");
        Assert.True(r.IsError);
        Assert.Equal(FormulaErrorKind.Number, r.ErrorKind);
    }

    // --- RAIZCUBICA -------------------------------------------------------

    [Theory]
    [InlineData("RAIZCUBICA(27)", 3)]
    [InlineData("RAIZCUBICA(-8)", -2)]
    [InlineData("RAIZCUBICA(0)", 0)]
    public void Raizcubica_computes_cube_root_including_negatives(string expr, double expected)
    {
        var r = Eval(expr);
        Assert.Equal(FormulaValueType.Number, r.Type);
        Assert.Equal((decimal)expected, r.AsNumber(), 6);
    }

    // --- TRUNCAR (número) -------------------------------------------------

    [Theory]
    [InlineData("TRUNCAR(3.9)", 3)]
    [InlineData("TRUNCAR(-3.9)", -3)]   // toward zero, no rounding
    [InlineData("TRUNCAR(3.14159; 2)", 3.14)]
    [InlineData("TRUNCAR(199.99; 0)", 199)]
    public void Truncar_number_drops_digits_without_rounding(string expr, double expected)
    {
        var r = Eval(expr);
        Assert.Equal(FormulaValueType.Number, r.Type);
        Assert.Equal((decimal)expected, r.AsNumber(), 6);
    }

    // --- TRUNCAR (texto) --------------------------------------------------

    [Fact]
    public void Truncar_text_keeps_first_n_characters()
    {
        var r = Eval("TRUNCAR(\"Rodobens\"; 4)");
        Assert.Equal(FormulaValueType.Text, r.Type);
        Assert.Equal("Rodo", r.AsText());
    }

    [Fact]
    public void Truncar_text_longer_than_string_returns_whole_string()
    {
        var r = Eval("TRUNCAR(\"abc\"; 10)");
        Assert.Equal("abc", r.AsText());
    }

    [Fact]
    public void Truncar_text_without_count_is_value_error()
    {
        var r = Eval("TRUNCAR(\"abc\")");
        Assert.True(r.IsError);
        Assert.Equal(FormulaErrorKind.Value, r.ErrorKind);
    }
}
