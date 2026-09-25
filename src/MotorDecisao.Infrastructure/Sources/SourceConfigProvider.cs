using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotorDecisao.Application.Sources;
using MotorDecisao.Infrastructure.Persistence;

namespace MotorDecisao.Infrastructure.Sources;

/// <summary>
/// Lê os parâmetros de execução de uma fonte da tabela <c>source_configs</c>.
/// Singleton que abre um escopo curto por leitura (o DbContext é scoped).
/// Retorna o padrão quando a fonte não tem configuração salva.
/// </summary>
public sealed class SourceConfigProvider : ISourceConfigProvider
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SourceConfigProvider(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<SourceParameters> GetAsync(string sourceName, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MotorDecisaoDbContext>();

        var cfg = await db.SourceConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.SourceName == sourceName, cancellationToken);

        if (cfg is null)
        {
            return SourceParameters.Default;
        }

        return new SourceParameters(
            MaxAttempts: cfg.MaxAttempts < 1 ? 1 : cfg.MaxAttempts,
            TimeoutSeconds: cfg.TimeoutSeconds <= 0 ? 30 : cfg.TimeoutSeconds,
            CacheTtlHours: cfg.CacheTtlHours);
    }
}
