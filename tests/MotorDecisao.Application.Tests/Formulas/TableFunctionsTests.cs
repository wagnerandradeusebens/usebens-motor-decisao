using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.PublishedFlows;
using Xunit;

namespace MotorDecisao.Application.Tests.Formulas;

/// <summary>
/// Testes de PROCV (busca exata) e PROCV.FAIXA (busca por faixa) sobre tabelas de
/// parâmetros semeadas no contexto. Cobre correspondência, valor padrão, erros e
/// coerção por tipo de coluna.
/// </summary>
public class TableFunctionsTests
{
    private static PublishedTableColumn Col(string name, string type) => new(name, type);

    private static IReadOnlyList<string> Row(params string[] cells) => cells;

    // Tabela taxa_uf: uf (Text, chave) -> taxa (Number), limite (Number).
    private static DictionaryFormulaContext WithTaxaUf(string? defaultValue = null)
    {
        var table = new PublishedTable(
            Name: "taxa_uf",
            Columns: new[] { Col("uf", "Text"), Col("taxa", "Number"), Col("limite", "Number") },
            Rows: new[] { Row("SP", "1.5", "30000"), Row("RJ", "1.8", "25000"), Row("MG", "1.6", "28000") },
            KeyColumn: "uf",
            MinColumn: null,
            MaxColumn: null,
            DefaultValue: defaultValue);
        return new DictionaryFormulaContext().SetTable(table);
    }

    // Tabela pontos_idade: idade_min/idade_max (faixa) -> pontos (Number).
    private static DictionaryFormulaContext WithPontosIdade(string? defaultValue = null)
    {
        var table = new PublishedTable(
            Name: "pontos_idade",
            Columns: new[] { Col("idade_min", "Number"), Col("idade_max", "Number"), Col("pontos", "Number") },
            Rows: new[] { Row("18", "25", "10"), Row("25", "40", "25"), Row("40", "200", "40") },
            KeyColumn: null,
            MinColumn: "idade_min",
            MaxColumn: "idade_max",
            DefaultValue: defaultValue);
        return new DictionaryFormulaContext().SetTable(table);
    }

    // --- Busca exata (PROCV) ----------------------------------------------

    [Fact]
    public void Procv_exact_text_key_returns_number_column()
    {
        var ctx = WithTaxaUf();
        Assert.Equal(1.8m, FormulaEngine.Evaluate("PROCV(\"taxa_uf\"; \"taxa\"; \"RJ\")", ctx).AsNumber());
    }

    [Fact]
    public void Procv_exact_reads_from_field_as_key()
    {
        var ctx = WithTaxaUf().Set("uf", FormulaValue.Text("SP"));
        Assert.Equal(30000m, FormulaEngine.Evaluate("PROCV(\"taxa_uf\"; \"limite\"; 'uf')", ctx).AsNumber());
    }

    [Fact]
    public void Procv_exact_key_case_insensitive()
    {
        var ctx = WithTaxaUf();
        Assert.Equal(1.6m, FormulaEngine.Evaluate("PROCV(\"taxa_uf\"; \"taxa\"; \"mg\")", ctx).AsNumber());
    }

    [Fact]
    public void Procv_numeric_key_matches_numeric_column()
    {
        // chave→valor com chave numérica.
        var table = new PublishedTable(
            Name: "parametros",
            Columns: new[] { Col("codigo", "Number"), Col("valor", "Number") },
            Rows: new[] { Row("1", "10"), Row("2", "20") },
            KeyColumn: "codigo", MinColumn: null, MaxColumn: null, DefaultValue: null);
        var ctx = new DictionaryFormulaContext().SetTable(table);
        Assert.Equal(20m, FormulaEngine.Evaluate("PROCV(\"parametros\"; \"valor\"; 2)", ctx).AsNumber());
    }

    // --- Busca por faixa (PROCV.FAIXA) ------------------------------------

    [Fact]
    public void Procv_faixa_finds_row_in_range()
    {
        var ctx = WithPontosIdade();
        Assert.Equal(25m, FormulaEngine.Evaluate("PROCV.FAIXA(\"pontos_idade\"; \"pontos\"; 30)", ctx).AsNumber());
    }

    [Fact]
    public void Procv_faixa_min_is_inclusive_max_is_exclusive()
    {
        var ctx = WithPontosIdade();
        // 25 pertence à faixa [25,40) → 25 pontos (não à [18,25)).
        Assert.Equal(25m, FormulaEngine.Evaluate("PROCV.FAIXA(\"pontos_idade\"; \"pontos\"; 25)", ctx).AsNumber());
        // 18 é o mínimo inclusivo da 1ª faixa → 10 pontos.
        Assert.Equal(10m, FormulaEngine.Evaluate("PROCV.FAIXA(\"pontos_idade\"; \"pontos\"; 18)", ctx).AsNumber());
    }

    // --- Valor padrão / não encontrado ------------------------------------

    [Fact]
    public void Procv_uses_default_value_when_no_match()
    {
        var ctx = WithTaxaUf(defaultValue: "0");
        // "ZZ" não existe → valor padrão "0".
        Assert.Equal("0", FormulaEngine.Evaluate("PROCV(\"taxa_uf\"; \"taxa\"; \"ZZ\")", ctx).AsText());
    }

    [Fact]
    public void Procv_returns_na_when_no_match_and_no_default()
    {
        var ctx = WithTaxaUf(defaultValue: null);
        Assert.True(FormulaEngine.Evaluate("PROCV(\"taxa_uf\"; \"taxa\"; \"ZZ\")", ctx).IsError);
    }

    [Fact]
    public void Procv_faixa_returns_default_when_value_out_of_all_ranges()
    {
        var ctx = WithPontosIdade(defaultValue: "0");
        // idade 10 está fora de todas as faixas (min 18).
        Assert.Equal("0", FormulaEngine.Evaluate("PROCV.FAIXA(\"pontos_idade\"; \"pontos\"; 10)", ctx).AsText());
    }

    // --- Erros -------------------------------------------------------------

    [Fact]
    public void Procv_unknown_table_is_name_error()
    {
        var ctx = WithTaxaUf();
        Assert.True(FormulaEngine.Evaluate("PROCV(\"inexistente\"; \"taxa\"; \"SP\")", ctx).IsError);
    }

    [Fact]
    public void Procv_unknown_return_column_is_name_error()
    {
        var ctx = WithTaxaUf();
        Assert.True(FormulaEngine.Evaluate("PROCV(\"taxa_uf\"; \"naoexiste\"; \"SP\")", ctx).IsError);
    }

    [Fact]
    public void Procv_wrong_arity_is_value_error()
    {
        var ctx = WithTaxaUf();
        Assert.True(FormulaEngine.Evaluate("PROCV(\"taxa_uf\"; \"taxa\")", ctx).IsError);
    }

    // --- Composição --------------------------------------------------------

    [Fact]
    public void Procv_result_composes_in_arithmetic()
    {
        var ctx = WithTaxaUf();
        // 1.8 (taxa RJ) * 100 = 180.
        Assert.Equal(180m, FormulaEngine.Evaluate("PROCV(\"taxa_uf\"; \"taxa\"; \"RJ\") * 100", ctx).AsNumber());
    }

    [Fact]
    public void Procv_text_column_returns_text()
    {
        var table = new PublishedTable(
            Name: "de_para",
            Columns: new[] { Col("codigo", "Text"), Col("descricao", "Text") },
            Rows: new[] { Row("A", "Aprovado"), Row("R", "Recusado") },
            KeyColumn: "codigo", MinColumn: null, MaxColumn: null, DefaultValue: null);
        var ctx = new DictionaryFormulaContext().SetTable(table);
        Assert.Equal("Recusado", FormulaEngine.Evaluate("PROCV(\"de_para\"; \"descricao\"; \"R\")", ctx).AsText());
    }
}
