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
    private readonly ISourceCache _cache;
    private readonly ISourceConfigProvider _config;

    public SourceCatalog(IEnumerable<IExternalSource> sources, ISourceCache cache, ISourceConfigProvider config)
    {
        _sources = sources.ToDictionary(s => s.Descriptor.Name, StringComparer.OrdinalIgnoreCase);
        _cache = cache;
        _config = config;
    }

    public IReadOnlyList<SourceDescriptor> List()
        => _sources.Values.Select(WithAvailabilityDatum).OrderBy(d => d.Name).ToList();

    public async Task<FormulaValue> ResolveAsync(
        ExternalRef reference,
        IFormulaContext context,
        CancellationToken cancellationToken = default)
        => (await ResolveWithOriginAsync(reference, context, cancellationToken)).Value;

    public async Task<SourceResolution> ResolveWithOriginAsync(
        ExternalRef reference,
        IFormulaContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_sources.TryGetValue(reference.Source, out var source))
        {
            // Unknown source behaves like an unknown name in a formula.
            return new SourceResolution(FormulaValue.Error(FormulaErrorKind.Name), SourceOrigin.Online);
        }

        // O dado de disponibilidade é status momentâneo — respondido na hora pelo
        // catálogo e NUNCA cacheado (pode mudar entre consultas). É sempre online.
        if (string.Equals(reference.Datum, SourceData.Availability, StringComparison.OrdinalIgnoreCase))
        {
            return new SourceResolution(FormulaValue.Boolean(source.IsAvailable(context)), SourceOrigin.Online);
        }

        var parameters = await _config.GetAsync(reference.Source, cancellationToken);

        // Chave de negócio = o campo obrigatório que identifica a consulta DESTA
        // fonte/produto (o KeyField do produto — ex.: cpf, cnpj, placa). Sem ele
        // no contexto não há como cachear de forma determinística: resolve o
        // produto direto na fonte (com retry/timeout) e devolve o dado pedido.
        var businessKey = BusinessKey(source, reference.Product, context);
        if (businessKey.Length == 0)
        {
            var direct = await ResolveProductWithRetryAsync(source, reference.Product, context, parameters, cancellationToken);
            var v = direct is null ? FormulaValue.Error(FormulaErrorKind.NotAvailable) : PickDatum(direct, reference.Datum);
            return new SourceResolution(v, SourceOrigin.Online);
        }

        // 1) Cache primeiro: se já temos a RESPOSTA do produto para esta chave
        //    (em qualquer política/execução) e não expirou, lê o dado do payload.
        var cached = await _cache.TryGetAsync(reference.Source, reference.Product, businessKey, parameters.CacheTtlHours, cancellationToken);
        if (cached is not null)
        {
            return new SourceResolution(PickDatum(cached, reference.Datum), SourceOrigin.Cache);
        }

        // 2) Miss: consulta a fonte UMA vez (produto inteiro) com retry/timeout,
        //    grava a resposta completa no cache e devolve o dado pedido.
        var data = await ResolveProductWithRetryAsync(source, reference.Product, context, parameters, cancellationToken);
        if (data is null)
        {
            return new SourceResolution(FormulaValue.Error(FormulaErrorKind.NotAvailable), SourceOrigin.Online);
        }
        if (data.Count > 0)
        {
            await _cache.SetAsync(reference.Source, reference.Product, businessKey, data, cancellationToken);
        }
        return new SourceResolution(PickDatum(data, reference.Datum), SourceOrigin.Online);
    }

    /// <summary>Lê um dado do mapa de resposta do produto; ausente → erro de nome.</summary>
    private static FormulaValue PickDatum(IReadOnlyDictionary<string, FormulaValue> data, string datum)
        => data.TryGetValue(datum, out var value) ? value : FormulaValue.Error(FormulaErrorKind.Name);

    /// <summary>
    /// Consulta o produto INTEIRO aplicando os parâmetros: até <c>MaxAttempts</c>
    /// tentativas, cada uma limitada a <c>TimeoutSeconds</c>. Retorna o mapa de
    /// dados no sucesso, ou <c>null</c> quando esgota as tentativas (erro/timeout)
    /// — o chamador transforma em <c>#N/D</c> para a fórmula decidir.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, FormulaValue>?> ResolveProductWithRetryAsync(
        IExternalSource source,
        string product,
        IFormulaContext context,
        SourceParameters parameters,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= parameters.MaxAttempts; attempt++)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(parameters.TimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try
            {
                return await source.ResolveProductAsync(product, context, linked.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout desta tentativa: tenta de novo se ainda houver tentativas.
                if (attempt == parameters.MaxAttempts)
                {
                    return null;
                }
            }
            catch (Exception) when (attempt < parameters.MaxAttempts)
            {
                // Falha transitória: nova tentativa.
            }
        }
        return null;
    }

    /// <summary>
    /// Chave de negócio da consulta: o valor do campo obrigatório declarado pelo
    /// produto (<see cref="SourceProduct.KeyField"/>) — não necessariamente o CPF.
    /// Ex.: um produto de CPF usa <c>cpf</c>; um de CNPJ, <c>cnpj</c>; um de veículo,
    /// <c>placa</c>. Ausente/vazio → chave vazia (não cacheia). Normaliza para tornar
    /// o lookup estável (ver <see cref="NormalizeKey"/>).
    /// </summary>
    private string BusinessKey(IExternalSource source, string product, IFormulaContext context)
    {
        var keyField = KeyFieldFor(source, product);
        if (keyField.Length == 0 ||
            !context.TryGetField(keyField, out var value) ||
            value.Type == FormulaValueType.Blank)
        {
            return string.Empty;
        }
        return NormalizeKey(value.AsText());
    }

    /// <summary>
    /// Descobre o campo-chave do produto referenciado. Se o produto não for
    /// encontrado, não há chave conhecida (não cacheia).
    /// </summary>
    private static string KeyFieldFor(IExternalSource source, string product)
    {
        var p = source.Descriptor.Products
            .FirstOrDefault(x => string.Equals(x.Name, product, StringComparison.OrdinalIgnoreCase));
        return p?.KeyField?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Normaliza o valor da chave para um lookup estável entre execuções. Para
    /// documentos/identificadores numéricos com máscara (CPF, CNPJ, placa só de
    /// números), reduz aos dígitos; para chaves textuais (ex.: placa Mercosul),
    /// apenas apara e coloca em maiúsculas. A regra: se houver dígitos e nenhuma
    /// letra, mantém só os dígitos; caso contrário, trim + upper-invariant.
    /// </summary>
    private static string NormalizeKey(string raw)
    {
        var trimmed = raw.Trim();
        var hasDigit = trimmed.Any(char.IsDigit);
        var hasLetter = trimmed.Any(char.IsLetter);
        return hasDigit && !hasLetter
            ? new string(trimmed.Where(char.IsDigit).ToArray())
            : trimmed.ToUpperInvariant();
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
