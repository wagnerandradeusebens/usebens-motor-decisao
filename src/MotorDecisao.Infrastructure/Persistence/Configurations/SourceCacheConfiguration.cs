using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MotorDecisao.Domain.Entities;

namespace MotorDecisao.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia o cache de respostas de fontes externas. Índice único na chave lógica
/// (fonte, produto, chave de negócio) para lookup rápido e upsert seguro; o
/// payload guarda a resposta inteira do produto (JSON com todos os dados).
/// </summary>
public class SourceCacheConfiguration : IEntityTypeConfiguration<SourceCacheEntry>
{
    public void Configure(EntityTypeBuilder<SourceCacheEntry> builder)
    {
        builder.ToTable("source_cache");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Source).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Product).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BusinessKey).HasMaxLength(200).IsRequired();
        // Payload é a resposta inteira do produto (JSON com todos os dados).
        builder.Property(x => x.Payload).HasMaxLength(16000).IsRequired();

        builder.HasIndex(x => new { x.Source, x.Product, x.BusinessKey }).IsUnique();
    }
}

/// <summary>Mapeia os parâmetros operacionais de uma fonte (por nome, único).</summary>
public class SourceConfigConfiguration : IEntityTypeConfiguration<SourceConfig>
{
    public void Configure(EntityTypeBuilder<SourceConfig> builder)
    {
        builder.ToTable("source_configs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.MaxAttempts).HasDefaultValue(1);
        builder.Property(x => x.TimeoutSeconds).HasDefaultValue(30);
        builder.Property(x => x.CacheTtlHours).HasDefaultValue(0);

        builder.HasIndex(x => x.SourceName).IsUnique();
    }
}
