using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.Formulas.Parsing;
using Xunit;

namespace MotorDecisao.Application.Tests.Formulas;

public class ExternalRefTests
{
    /// <summary>Context with a pre-fetched external value for the SERASA score.</summary>
    private static DictionaryFormulaContext ContextWithScore(decimal score)
        => new DictionaryFormulaContext()
            .SetExternal("SERASA", "Score", "Pontuacao", FormulaValue.Number(score));

    [Fact]
    public void Parses_and_evaluates_external_reference()
    {
        var ctx = ContextWithScore(750);
        var result = FormulaEngine.Evaluate("[SERASA;Score;Pontuacao]", ctx);
        Assert.Equal(750m, result.AsNumber());
    }

    [Fact]
    public void External_reference_usable_in_expression()
    {
        var ctx = ContextWithScore(720);
        var result = FormulaEngine.Evaluate("SE([SERASA;Score;Pontuacao] >= 700; \"APROVAR\"; \"NEGAR\")", ctx);
        Assert.Equal("APROVAR", result.AsText());
    }

    [Fact]
    public void Compiled_formula_reports_external_references()
    {
        var compiled = FormulaEngine.Compile("[SERASA;Score;Pontuacao] + 10");
        Assert.Single(compiled.ExternalReferences);
        var reference = compiled.ExternalReferences[0];
        Assert.Equal("SERASA", reference.Source);
        Assert.Equal("Score", reference.Product);
        Assert.Equal("Pontuacao", reference.Datum);
    }

    [Fact]
    public void Not_prefetched_external_reference_is_na_error()
    {
        // Empty context: nothing pre-fetched -> #N/D.
        var result = FormulaEngine.Evaluate("[SERASA;Score;Pontuacao]", new DictionaryFormulaContext());
        Assert.True(result.IsError);
        Assert.Equal(FormulaErrorKind.NotAvailable, result.ErrorKind);
    }

    [Theory]
    [InlineData("[SERASA;Score]")]            // only 2 parts
    [InlineData("[SERASA;Score;Pontuacao;X]")] // 4 parts
    [InlineData("[SERASA;Score;Pontuacao")]   // unclosed
    public void Malformed_external_reference_is_syntax_error(string expr)
    {
        Assert.Throws<FormulaException>(() => FormulaEngine.Compile(expr));
    }
}
