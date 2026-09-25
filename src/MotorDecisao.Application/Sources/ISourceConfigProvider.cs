namespace MotorDecisao.Application.Sources;

/// <summary>Parâmetros operacionais de uma fonte (imutável, para o resolver).</summary>
public sealed record SourceParameters(int MaxAttempts, int TimeoutSeconds, int CacheTtlHours)
{
    /// <summary>Padrão quando a fonte não tem configuração salva.</summary>
    public static readonly SourceParameters Default = new(MaxAttempts: 1, TimeoutSeconds: 30, CacheTtlHours: 0);
}

/// <summary>
/// Fornece os parâmetros de execução de uma fonte (tentativas, timeout, TTL do
/// cache), lidos da configuração persistida. Retorna o padrão quando não há
/// configuração para a fonte.
/// </summary>
public interface ISourceConfigProvider
{
    Task<SourceParameters> GetAsync(string sourceName, CancellationToken cancellationToken = default);
}
