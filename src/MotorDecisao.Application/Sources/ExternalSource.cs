namespace MotorDecisao.Application.Sources;

/// <summary>
/// A reference to a single external datum in a formula, written as
/// <c>[Fonte;Produto;Dado]</c> (e.g. <c>[SERASA;Score;Pontuacao]</c>).
/// </summary>
public readonly record struct ExternalRef(string Source, string Product, string Datum)
{
    public override string ToString() => $"[{Source};{Product};{Datum}]";
}

/// <summary>
/// Uma referência a outra política numa fórmula, escrita como
/// <c>(Política;Categoria;Variável)</c>. Categoria ∈ {Pontos, Limite, Resposta,
/// Variaveis}. Variable só é usada quando a categoria é Variaveis.
/// </summary>
public readonly record struct PolicyRef(string Policy, string Category, string Variable)
{
    public override string ToString() =>
        string.IsNullOrEmpty(Variable) ? $"({Policy};{Category})" : $"({Policy};{Category};{Variable})";
}

/// <summary>Describes one datum a product exposes (e.g. "Pontuacao").</summary>
public sealed record SourceDatum(string Name, string? Description = null);

/// <summary>
/// A product of a source (e.g. SERASA's "Score") and the data it returns.
/// <para>
/// <see cref="KeyField"/> is the proposal field that is <b>required</b> to query
/// this product (its natural key): for a CPF bureau it is <c>cpf</c>, for a CNPJ
/// product <c>cnpj</c>, for a vehicle product <c>placa</c>, and so on. The engine
/// reads that field from the proposal and uses its value as the cache business
/// key, so the cache is shared per (source, product, key value) — never assuming
/// the key is always the CPF.
/// </para>
/// </summary>
public sealed record SourceProduct(
    string Name,
    IReadOnlyList<SourceDatum> Data,
    string? Description = null,
    string KeyField = "cpf");

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
    /// Resolves the WHOLE product in a single call, returning every datum it
    /// exposes as a map (datum name → value). This mirrors a real bureau call
    /// (one request returns the entire product), which is what the engine caches:
    /// one entry per (source, product, key) holding all data from the same
    /// snapshot. Returns an empty/partial map for an unknown product.
    /// </summary>
    Task<IReadOnlyDictionary<string, Formulas.FormulaValue>> ResolveProductAsync(
        string product,
        Formulas.IFormulaContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves one datum for the given product. Defaults to calling
    /// <see cref="ResolveProductAsync"/> and picking the datum from the map, so a
    /// source only has to implement the whole-product call. Returns a name error
    /// if the product/datum is unknown.
    /// </summary>
    async Task<Formulas.FormulaValue> ResolveAsync(
        string product,
        string datum,
        Formulas.IFormulaContext context,
        CancellationToken cancellationToken = default)
    {
        var data = await ResolveProductAsync(product, context, cancellationToken);
        foreach (var kvp in data)
        {
            if (string.Equals(kvp.Key, datum, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }
        return Formulas.FormulaValue.Error(Formulas.FormulaErrorKind.Name);
    }
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
