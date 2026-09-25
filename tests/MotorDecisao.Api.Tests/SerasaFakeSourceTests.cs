using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.Sources;
using MotorDecisao.Infrastructure.Sources;
using Xunit;

namespace MotorDecisao.Api.Tests;

public class SerasaFakeSourceTests
{
    [Fact]
    public async Task Resolves_score_between_1_and_1000()
    {
        IExternalSource source = new SerasaFakeSource();
        var ctx = new DictionaryFormulaContext().Set("cpf", FormulaValue.Text("11493903799"));

        // Sample several times since the value is random.
        for (var i = 0; i < 200; i++)
        {
            var value = await source.ResolveAsync("Score", "Pontuacao", ctx);
            Assert.Equal(FormulaValueType.Number, value.Type);
            var score = value.AsNumber();
            Assert.InRange(score, 1m, 1000m);
        }
    }

    [Fact]
    public async Task Unknown_product_or_datum_is_name_error()
    {
        IExternalSource source = new SerasaFakeSource();
        var ctx = new DictionaryFormulaContext();
        var value = await source.ResolveAsync("Outro", "X", ctx);
        Assert.True(value.IsError);
        Assert.Equal(FormulaErrorKind.Name, value.ErrorKind);
    }

    [Fact]
    public void Descriptor_exposes_score_pontuacao()
    {
        var d = new SerasaFakeSource().Descriptor;
        Assert.Equal("SERASA", d.Name);
        var product = Assert.Single(d.Products);
        Assert.Equal("Score", product.Name);
        Assert.Contains(product.Data, x => x.Name == "Pontuacao");
    }

    [Fact]
    public void Catalog_lists_registered_sources()
    {
        var catalog = new SourceCatalog(new IExternalSource[] { new SerasaFakeSource() }, new NullSourceCache(), new NullSourceConfigProvider());
        var list = catalog.List();
        Assert.Contains(list, s => s.Name == "SERASA");
    }
}
