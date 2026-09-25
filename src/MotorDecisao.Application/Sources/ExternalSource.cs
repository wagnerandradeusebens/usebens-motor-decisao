namespace MotorDecisao.Application.Sources;

/// <summary>
/// A reference to a single external datum in a formula, written as
/// <c>[Fonte;Produto;Dado]</c> (e.g. <c>[SERASA;Score;Pontuacao]</c>).
/// </summary>
public readonly record struct ExternalRef(string Source, string Product, string Datum)
{
    public override string ToString() => $"[{Source};{Product};{Datum}]";
}

/// <summary>Describes one datum a product exposes (e.g. "Pontuacao").</summary>
public sealed record SourceDatum(string Name, string? Description = null);

/// <summary>A product of a source (e.g. SERASA's "Score") and the data it returns.</summary>
public sealed record SourceProduct(string Name, IReadOnlyList<SourceDatum> Data, string? Description = null);

/// <summary>
/// Catalog metadata for a registered external integration (bureau/API). This is
/// the read-only shape the "Fontes" menu and the formula autocomplete consume.
/// </summary>
public sealed record SourceDescriptor(string Name, IReadOnlyList<SourceProduct> Products, string? Description = null);

/// <summary>
/// A registered external integration. Each real integration (SERASA, etc.)
/// implements this and self-registers, so the catalog only ever lists sources
/// that actually exist. Users do not create sources from the UI.
/// </summary>
public interface IExternalSource
{
    /// <summary>Catalog metadata (name, products, data fields).</summary>
    SourceDescriptor Descriptor { get; }

    /// <summary>
    /// Whether the source is currently reachable/answering. Exposed to formulas
    /// via the reserved datum <see cref="SourceData.Availability"/> on any product
    /// (e.g. <c>[SERASA;Score;Disponibilidade]</c>), so a policy can branch when a
    /// bureau is down. Defaults to always-available.
    /// </summary>
    bool IsAvailable(Formulas.IFormulaContext context) => true;

    /// <summary>
    /// Resolves one datum for the given product using the proposal context (for
    /// example, reading the <c>cpf</c> field). Returns an error value if the
    /// product/datum is unknown.
    /// </summary>
    Task<Formulas.FormulaValue> ResolveAsync(
        string product,
        string datum,
        Formulas.IFormulaContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Reserved datum names available on every source product.</summary>
public static class SourceData
{
    /// <summary>
    /// Boolean datum, present on every product, telling whether the source is
    /// currently available. Written in formulas as <c>[Fonte;Produto;Disponibilidade]</c>.
    /// </summary>
    public const string Availability = "Disponibilidade";
}

/// <summary>
/// The set of registered sources. Lists them for the catalog/autocomplete and
/// resolves an <see cref="ExternalRef"/> to a value at decision time.
/// </summary>
public interface ISourceCatalog
{
    /// <summary>All registered sources' metadata.</summary>
    IReadOnlyList<SourceDescriptor> List();

    /// <summary>
    /// Resolves an external reference to a value. Unknown source yields a
    /// <c>#NOME?</c> error so it surfaces like an unknown name in a formula.
    /// </summary>
    Task<Formulas.FormulaValue> ResolveAsync(
        ExternalRef reference,
        Formulas.IFormulaContext context,
        CancellationToken cancellationToken = default);
}
