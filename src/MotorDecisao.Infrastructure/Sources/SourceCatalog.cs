using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.Sources;

namespace MotorDecisao.Infrastructure.Sources;

/// <summary>
/// Aggregates all registered <see cref="IExternalSource"/> integrations. Sources
/// are discovered by DI (each real integration self-registers), so the catalog
/// only ever exposes integrations that actually exist.
/// </summary>
public sealed class SourceCatalog : ISourceCatalog
{
    private readonly Dictionary<string, IExternalSource> _sources;

    public SourceCatalog(IEnumerable<IExternalSource> sources)
    {
        _sources = sources.ToDictionary(s => s.Descriptor.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<SourceDescriptor> List()
        => _sources.Values.Select(WithAvailabilityDatum).OrderBy(d => d.Name).ToList();

    public Task<FormulaValue> ResolveAsync(
        ExternalRef reference,
        IFormulaContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_sources.TryGetValue(reference.Source, out var source))
        {
            // Unknown source behaves like an unknown name in a formula.
            return Task.FromResult(FormulaValue.Error(FormulaErrorKind.Name));
        }

        // The availability datum is reserved and answered by the catalog for every
        // product, so a policy can check a source before consuming its data.
        if (string.Equals(reference.Datum, SourceData.Availability, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(FormulaValue.Boolean(source.IsAvailable(context)));
        }

        return source.ResolveAsync(reference.Product, reference.Datum, context, cancellationToken);
    }

    /// <summary>
    /// Returns the source's descriptor with the reserved <c>Disponibilidade</c>
    /// datum appended to every product, so the catalog/autocomplete advertises it.
    /// </summary>
    private static SourceDescriptor WithAvailabilityDatum(IExternalSource source)
    {
        var d = source.Descriptor;
        var products = d.Products.Select(p =>
        {
            if (p.Data.Any(x => string.Equals(x.Name, SourceData.Availability, StringComparison.OrdinalIgnoreCase)))
            {
                return p;
            }
            var data = p.Data.Append(new SourceDatum(SourceData.Availability, "Indica se a fonte está disponível (VERDADEIRO/FALSO).")).ToList();
            return p with { Data = data };
        }).ToList();
        return d with { Products = products };
    }
}
