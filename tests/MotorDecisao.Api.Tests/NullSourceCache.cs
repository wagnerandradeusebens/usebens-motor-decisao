using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.Sources;

namespace MotorDecisao.Api.Tests;

/// <summary>
/// Cache de fontes no-op para os testes: sempre "miss" (retorna null) e não grava.
/// Faz o SourceCatalog resolver sempre direto na fonte, sem tocar em banco.
/// </summary>
public sealed class NullSourceCache : ISourceCache
{
    public Task<IReadOnlyDictionary<string, FormulaValue>?> TryGetAsync(
        string source, string product, string businessKey, int ttlHours, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, FormulaValue>?>(null);

    public Task SetAsync(
        string source, string product, string businessKey,
        IReadOnlyDictionary<string, FormulaValue> data, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>Provedor de parâmetros no-op para testes: sempre o padrão.</summary>
public sealed class NullSourceConfigProvider : ISourceConfigProvider
{
    public Task<SourceParameters> GetAsync(string sourceName, CancellationToken cancellationToken = default)
        => Task.FromResult(SourceParameters.Default);
}
