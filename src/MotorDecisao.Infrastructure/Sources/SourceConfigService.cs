using Microsoft.EntityFrameworkCore;
using MotorDecisao.Application.Sources;
using MotorDecisao.Domain.Entities;
using MotorDecisao.Infrastructure.Persistence;

namespace MotorDecisao.Infrastructure.Sources;

/// <summary>CRUD dos parâmetros de fonte (scoped: usa o DbContext diretamente).</summary>
public sealed class SourceConfigService : ISourceConfigService
{
    private readonly MotorDecisaoDbContext _db;

    public SourceConfigService(MotorDecisaoDbContext db) => _db = db;

    public async Task<SourceParameters> GetAsync(string sourceName, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.SourceConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.SourceName == sourceName, cancellationToken);

        return cfg is null
            ? SourceParameters.Default
            : new SourceParameters(cfg.MaxAttempts, cfg.TimeoutSeconds, cfg.CacheTtlHours);
    }

    public async Task<SourceParameters> UpsertAsync(
        string sourceName,
        SourceParameters parameters,
        CancellationToken cancellationToken = default)
    {
        // Normaliza limites mínimos.
        var maxAttempts = parameters.MaxAttempts < 1 ? 1 : parameters.MaxAttempts;
        var timeout = parameters.TimeoutSeconds <= 0 ? 30 : parameters.TimeoutSeconds;
        var ttl = parameters.CacheTtlHours < 0 ? 0 : parameters.CacheTtlHours;

        var cfg = await _db.SourceConfigs.FirstOrDefaultAsync(c => c.SourceName == sourceName, cancellationToken);
        if (cfg is null)
        {
            cfg = new SourceConfig { SourceName = sourceName };
            _db.SourceConfigs.Add(cfg);
        }
        cfg.MaxAttempts = maxAttempts;
        cfg.TimeoutSeconds = timeout;
        cfg.CacheTtlHours = ttl;

        await _db.SaveChangesAsync(cancellationToken);
        return new SourceParameters(maxAttempts, timeout, ttl);
    }
}
