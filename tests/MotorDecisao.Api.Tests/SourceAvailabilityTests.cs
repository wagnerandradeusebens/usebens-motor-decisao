using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.Sources;
using MotorDecisao.Infrastructure.Sources;
using Xunit;

namespace MotorDecisao.Api.Tests;

/// <summary>
/// The reserved "Disponibilidade" datum must be answered by the catalog for every
/// source/product as a boolean, and advertised in the catalog metadata.
/// </summary>
public class SourceAvailabilityTests
{
    /// <summary>A source that is always available, to make the assertion deterministic.</summary>
    private sealed class AlwaysUpSource : IExternalSource
    {
        public SourceDescriptor Descriptor { get; } = new(
            Name: "TESTE",
            Products: new[] { new SourceProduct("Prod", new[] { new SourceDatum("Dado") }) });

        public bool IsAvailable(IFormulaContext context) => true;

        public Task<IReadOnlyDictionary<string, FormulaValue>> ResolveProductAsync(string product, IFormulaContext context, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, FormulaValue>>(
                new Dictionary<string, FormulaValue>(StringComparer.OrdinalIgnoreCase) { ["Dado"] = FormulaValue.Number(1) });
    }

    private sealed class AlwaysDownSource : IExternalSource
    {
        public SourceDescriptor Descriptor { get; } = new(
            Name: "OFFLINE",
            Products: new[] { new SourceProduct("Prod", new[] { new SourceDatum("Dado") }) });

        public bool IsAvailable(IFormulaContext context) => false;

        public Task<IReadOnlyDictionary<string, FormulaValue>> ResolveProductAsync(string product, IFormulaContext context, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, FormulaValue>>(
                new Dictionary<string, FormulaValue>(StringComparer.OrdinalIgnoreCase) { ["Dado"] = FormulaValue.Number(1) });
    }

    [Fact]
    public async Task Availability_datum_returns_boolean_true_when_up()
    {
        var catalog = new SourceCatalog(new IExternalSource[] { new AlwaysUpSource() }, new NullSourceCache(), new NullSourceConfigProvider());
        var value = await catalog.ResolveAsync(new ExternalRef("TESTE", "Prod", "Disponibilidade"), new DictionaryFormulaContext());
        Assert.Equal(FormulaValueType.Boolean, value.Type);
        Assert.True(value.AsBoolean());
    }

    [Fact]
    public async Task Availability_datum_returns_boolean_false_when_down()
    {
        var catalog = new SourceCatalog(new IExternalSource[] { new AlwaysDownSource() }, new NullSourceCache(), new NullSourceConfigProvider());
        var value = await catalog.ResolveAsync(new ExternalRef("OFFLINE", "Prod", "Disponibilidade"), new DictionaryFormulaContext());
        Assert.Equal(FormulaValueType.Boolean, value.Type);
        Assert.False(value.AsBoolean());
    }

    [Fact]
    public void Catalog_advertises_availability_datum_on_every_product()
    {
        var catalog = new SourceCatalog(new IExternalSource[] { new SerasaFakeSource(), new BacenFakeSource() }, new NullSourceCache(), new NullSourceConfigProvider());
        foreach (var source in catalog.List())
        {
            foreach (var product in source.Products)
            {
                Assert.Contains(product.Data, d => d.Name == "Disponibilidade");
            }
        }
    }

    [Fact]
    public async Task Fake_sources_answer_availability_as_boolean()
    {
        var catalog = new SourceCatalog(new IExternalSource[] { new SerasaFakeSource(), new BacenFakeSource() }, new NullSourceCache(), new NullSourceConfigProvider());
        var ctx = new DictionaryFormulaContext().Set("cpf", FormulaValue.Text("11493903799"));

        var serasa = await catalog.ResolveAsync(new ExternalRef("SERASA", "Score", "Disponibilidade"), ctx);
        var bacen = await catalog.ResolveAsync(new ExternalRef("BACEN", "SCR", "Disponibilidade"), ctx);

        Assert.Equal(FormulaValueType.Boolean, serasa.Type);
        Assert.Equal(FormulaValueType.Boolean, bacen.Type);
    }
}
