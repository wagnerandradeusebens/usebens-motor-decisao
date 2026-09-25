using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.Sources;
using MotorDecisao.Infrastructure.Sources;
using Xunit;

namespace MotorDecisao.Api.Tests;

/// <summary>
/// A chave de cache é o campo OBRIGATÓRIO que identifica a consulta do produto
/// (o <see cref="SourceProduct.KeyField"/>), não necessariamente o CPF. E o cache
/// guarda a RESPOSTA inteira do produto: uma única chamada à fonte serve todos os
/// dados daquele produto/chave.
/// </summary>
public class SourceCacheKeyTests
{
    [Fact]
    public async Task Cache_is_keyed_by_the_products_required_field()
    {
        // Fonte cuja chave de consulta é "placa" (não cpf).
        var source = new KeyedFakeSource(product: "Consulta", keyField: "placa");
        var cache = new RecordingCache();
        var catalog = new SourceCatalog(new IExternalSource[] { source }, cache, new NullSourceConfigProvider());

        var ctx = new DictionaryFormulaContext()
            .Set("cpf", FormulaValue.Text("11111111111"))   // presente, mas NÃO é a chave
            .Set("placa", FormulaValue.Text("ABC1D23"));      // esta é a chave do produto

        var result = await catalog.ResolveAsync(new ExternalRef("FIPE", "Consulta", "Valor"), ctx);

        Assert.Equal(42m, result.AsNumber());
        // O cache foi consultado e gravado com a placa normalizada (upper), não o CPF.
        Assert.Equal("ABC1D23", cache.LastGetKey);
        Assert.Equal("ABC1D23", cache.LastSetKey);
    }

    [Fact]
    public async Task Without_the_required_field_it_does_not_cache()
    {
        var source = new KeyedFakeSource(product: "Consulta", keyField: "placa");
        var cache = new RecordingCache();
        var catalog = new SourceCatalog(new IExternalSource[] { source }, cache, new NullSourceConfigProvider());

        // Sem "placa" no contexto, não há chave determinística: resolve sem cachear.
        var ctx = new DictionaryFormulaContext().Set("cpf", FormulaValue.Text("11111111111"));
        var result = await catalog.ResolveAsync(new ExternalRef("FIPE", "Consulta", "Valor"), ctx);

        Assert.Equal(42m, result.AsNumber());
        Assert.Null(cache.LastGetKey);
        Assert.Null(cache.LastSetKey);
    }

    [Fact]
    public async Task Whole_product_is_cached_so_a_second_datum_needs_no_new_call()
    {
        // Fonte que conta quantas vezes é consultada.
        var source = new CountingProductSource(keyField: "cpf");
        // Cache em memória: grava e devolve a resposta inteira do produto.
        var cache = new InMemoryProductCache();
        var catalog = new SourceCatalog(new IExternalSource[] { source }, cache, new NullSourceConfigProvider());
        var ctx = new DictionaryFormulaContext().Set("cpf", FormulaValue.Text("529.982.247-25"));

        // 1ª referência: miss → 1 chamada à fonte, grava o produto inteiro.
        var a = await catalog.ResolveAsync(new ExternalRef("BUREAU", "Prod", "A"), ctx);
        // 2ª referência a OUTRO dado do mesmo produto/chave: hit → sem nova chamada.
        var b = await catalog.ResolveAsync(new ExternalRef("BUREAU", "Prod", "B"), ctx);

        Assert.Equal(10m, a.AsNumber());
        Assert.Equal(20m, b.AsNumber());
        Assert.Equal(1, source.Calls); // uma única consulta à fonte serviu os dois dados
    }

    /// <summary>Fonte fake com produto/keyField parametrizáveis; retorna Valor=42.</summary>
    private sealed class KeyedFakeSource : IExternalSource
    {
        public SourceDescriptor Descriptor { get; }

        public KeyedFakeSource(string product, string keyField)
        {
            Descriptor = new SourceDescriptor(
                Name: "FIPE",
                Products: new[]
                {
                    new SourceProduct(Name: product, Data: new[] { new SourceDatum("Valor") }, KeyField: keyField),
                });
        }

        public Task<IReadOnlyDictionary<string, FormulaValue>> ResolveProductAsync(
            string product, IFormulaContext context, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, FormulaValue>>(
                new Dictionary<string, FormulaValue>(StringComparer.OrdinalIgnoreCase) { ["Valor"] = FormulaValue.Number(42m) });
    }

    /// <summary>Fonte que conta chamadas e devolve dois dados (A=10, B=20) por consulta.</summary>
    private sealed class CountingProductSource : IExternalSource
    {
        public int Calls { get; private set; }
        public SourceDescriptor Descriptor { get; }

        public CountingProductSource(string keyField)
        {
            Descriptor = new SourceDescriptor(
                Name: "BUREAU",
                Products: new[]
                {
                    new SourceProduct(
                        Name: "Prod",
                        Data: new[] { new SourceDatum("A"), new SourceDatum("B") },
                        KeyField: keyField),
                });
        }

        public Task<IReadOnlyDictionary<string, FormulaValue>> ResolveProductAsync(
            string product, IFormulaContext context, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyDictionary<string, FormulaValue>>(
                new Dictionary<string, FormulaValue>(StringComparer.OrdinalIgnoreCase)
                {
                    ["A"] = FormulaValue.Number(10m),
                    ["B"] = FormulaValue.Number(20m),
                });
        }
    }

    /// <summary>Cache que registra a última chave usada em get/set (sem persistir nada).</summary>
    private sealed class RecordingCache : ISourceCache
    {
        public string? LastGetKey { get; private set; }
        public string? LastSetKey { get; private set; }

        public Task<IReadOnlyDictionary<string, FormulaValue>?> TryGetAsync(
            string source, string product, string businessKey, int ttlHours, CancellationToken ct = default)
        {
            LastGetKey = businessKey;
            return Task.FromResult<IReadOnlyDictionary<string, FormulaValue>?>(null); // sempre miss
        }

        public Task SetAsync(
            string source, string product, string businessKey,
            IReadOnlyDictionary<string, FormulaValue> data, CancellationToken ct = default)
        {
            LastSetKey = businessKey;
            return Task.CompletedTask;
        }
    }

    /// <summary>Cache em memória por (fonte, produto, chave) da resposta inteira.</summary>
    private sealed class InMemoryProductCache : ISourceCache
    {
        private readonly Dictionary<string, IReadOnlyDictionary<string, FormulaValue>> _store = new();

        private static string Key(string source, string product, string businessKey)
            => $"{source}|{product}|{businessKey}";

        public Task<IReadOnlyDictionary<string, FormulaValue>?> TryGetAsync(
            string source, string product, string businessKey, int ttlHours, CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue(Key(source, product, businessKey), out var data) ? data : null);

        public Task SetAsync(
            string source, string product, string businessKey,
            IReadOnlyDictionary<string, FormulaValue> data, CancellationToken ct = default)
        {
            _store[Key(source, product, businessKey)] = data;
            return Task.CompletedTask;
        }
    }
}
