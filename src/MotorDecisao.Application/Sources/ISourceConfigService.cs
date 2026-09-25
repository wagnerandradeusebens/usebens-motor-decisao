namespace MotorDecisao.Application.Sources;

/// <summary>
/// Leitura/escrita dos parâmetros operacionais de uma fonte (tela de Fontes).
/// Diferente do <see cref="ISourceConfigProvider"/> (que só lê, no hot path da
/// execução), este é o CRUD usado pela API de configuração.
/// </summary>
public interface ISourceConfigService
{
    /// <summary>Parâmetros salvos da fonte, ou o padrão quando não configurada.</summary>
    Task<SourceParameters> GetAsync(string sourceName, CancellationToken cancellationToken = default);

    /// <summary>Cria ou atualiza os parâmetros da fonte.</summary>
    Task<SourceParameters> UpsertAsync(
        string sourceName,
        SourceParameters parameters,
        CancellationToken cancellationToken = default);
}
