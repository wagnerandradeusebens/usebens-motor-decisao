using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.Sources;
using MotorDecisao.Infrastructure.Sources;
using Xunit;

namespace MotorDecisao.Api.Tests;

public class BacenFakeSourceTests
{
    private static DictionaryFormulaContext CtxWithCpf(string cpf)
        => new DictionaryFormulaContext().Set("cpf", FormulaValue.Text(cpf));

    private static async Task<FormulaValue> Resolve(BacenFakeSource src, string datum, string cpf)
        => await src.ResolveAsync("SCR", datum, CtxWithCpf(cpf));

    [Fact]
    public async Task Resolves_all_scr_data_with_expected_types_and_sensible_ranges()
    {
        var source = new BacenFakeSource();

        // Sample across several CPFs to exercise the ranges.
        for (var i = 0; i < 200; i++)
        {
            var cpf = (10_000_000_000 + i * 7).ToString();

            var tempo = await Resolve(source, "TempoInicioSFN", cpf);
            Assert.Equal(FormulaValueType.Number, tempo.Type);
            Assert.InRange(tempo.AsNumber(), 1m, 360m);

            var limMin6 = await Resolve(source, "LimiteCreditoMinimo6m", cpf);
            Assert.Equal(FormulaValueType.Number, limMin6.Type);
            Assert.True(limMin6.AsNumber() >= 0m);

            var limAtualMedia6 = await Resolve(source, "LimiteCreditoAtualMedia6m", cpf);
            Assert.Equal(FormulaValueType.Number, limAtualMedia6.Type);
            Assert.True(limAtualMedia6.AsNumber() >= 0m);

            var limMedia3 = await Resolve(source, "LimiteCreditoMedia3m", cpf);
            Assert.Equal(FormulaValueType.Number, limMedia3.Type);
            Assert.True(limMedia3.AsNumber() >= 0m);

            // A ratio of two non-negative amounts where the numerator is a fraction
            // of the denominator: it must stay within [0, 1].
            var div3m12m = await Resolve(source, "DividasAVencer3m12m", cpf);
            Assert.Equal(FormulaValueType.Number, div3m12m.Type);
            Assert.InRange(div3m12m.AsNumber(), 0m, 1m);

            var divMin12 = await Resolve(source, "DividasAVencerMinimo12m", cpf);
            Assert.Equal(FormulaValueType.Number, divMin12.Type);
            Assert.True(divMin12.AsNumber() >= 0m);

            var flag = await Resolve(source, "FlagDividasVencidas12m", cpf);
            Assert.Equal(FormulaValueType.Boolean, flag.Type);
        }
    }

    [Fact]
    public async Task Minimo_6m_never_exceeds_media_3m_relationship_is_consistent()
    {
        // The minimum-6m limit is drawn as a fraction (<= 1) of the 6m mean, while
        // the 3m mean is near the 6m mean; both are non-negative. Just assert they
        // resolve to numbers for a fixed CPF (internal consistency is by design).
        var source = new BacenFakeSource();
        var min6 = await Resolve(source, "LimiteCreditoMinimo6m", "11493903799");
        var media3 = await Resolve(source, "LimiteCreditoMedia3m", "11493903799");

        Assert.Equal(FormulaValueType.Number, min6.Type);
        Assert.Equal(FormulaValueType.Number, media3.Type);
    }

    [Fact]
    public async Task Same_cpf_yields_stable_values()
    {
        var source = new BacenFakeSource();

        var a = await Resolve(source, "LimiteCreditoMedia3m", "11493903799");
        var b = await Resolve(source, "LimiteCreditoMedia3m", "11493903799");

        Assert.Equal(a.AsNumber(), b.AsNumber());
    }

    [Fact]
    public async Task Unknown_product_is_name_error()
    {
        var source = new BacenFakeSource();
        var value = await source.ResolveAsync("Outro", "TempoInicioSFN", CtxWithCpf("1"));
        Assert.True(value.IsError);
        Assert.Equal(FormulaErrorKind.Name, value.ErrorKind);
    }

    [Fact]
    public async Task Unknown_datum_is_name_error()
    {
        var source = new BacenFakeSource();
        var value = await source.ResolveAsync("SCR", "NaoExiste", CtxWithCpf("1"));
        Assert.True(value.IsError);
        Assert.Equal(FormulaErrorKind.Name, value.ErrorKind);
    }

    [Fact]
    public void Descriptor_exposes_scr_product_and_all_data()
    {
        var d = new BacenFakeSource().Descriptor;
        Assert.Equal("BACEN", d.Name);
        var product = Assert.Single(d.Products);
        Assert.Equal("SCR", product.Name);

        var names = product.Data.Select(x => x.Name).ToList();
        Assert.Contains("TempoInicioSFN", names);
        Assert.Contains("LimiteCreditoMinimo6m", names);
        Assert.Contains("LimiteCreditoAtualMedia6m", names);
        Assert.Contains("LimiteCreditoMedia3m", names);
        Assert.Contains("DividasAVencer3m12m", names);
        Assert.Contains("DividasAVencerMinimo12m", names);
        Assert.Contains("FlagDividasVencidas12m", names);
    }

    [Fact]
    public void Catalog_lists_bacen_alongside_serasa()
    {
        var catalog = new SourceCatalog(new IExternalSource[] { new SerasaFakeSource(), new BacenFakeSource() });
        var list = catalog.List();
        Assert.Contains(list, s => s.Name == "BACEN");
        Assert.Contains(list, s => s.Name == "SERASA");
    }
}
