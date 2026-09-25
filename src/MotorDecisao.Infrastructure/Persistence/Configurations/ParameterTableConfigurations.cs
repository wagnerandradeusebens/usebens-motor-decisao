using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MotorDecisao.Domain.Entities;

namespace MotorDecisao.Infrastructure.Persistence.Configurations;

/// <summary>Mapeia uma tabela de parâmetros LOCAL (escopada à versão).</summary>
public class ParameterTableConfiguration : IEntityTypeConfiguration<ParameterTable>
{
    public void Configure(EntityTypeBuilder<ParameterTable> builder)
    {
        builder.ToTable("parameter_tables");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ColumnsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.RowsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.KeyColumn).HasMaxLength(100);
        builder.Property(x => x.MinColumn).HasMaxLength(100);
        builder.Property(x => x.MaxColumn).HasMaxLength(100);
        builder.Property(x => x.DefaultValue).HasMaxLength(2000);

        // Nome único por versão (mesma regra das variáveis locais).
        builder.HasIndex(x => new { x.FlowVersionId, x.Name }).IsUnique();

        builder.HasOne(x => x.FlowVersion)
            .WithMany()
            .HasForeignKey(x => x.FlowVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Mapeia uma tabela de parâmetros GLOBAL (compartilhada).</summary>
public class GlobalParameterTableConfiguration : IEntityTypeConfiguration<GlobalParameterTable>
{
    public void Configure(EntityTypeBuilder<GlobalParameterTable> builder)
    {
        builder.ToTable("global_parameter_tables");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ColumnsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.RowsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.KeyColumn).HasMaxLength(100);
        builder.Property(x => x.MinColumn).HasMaxLength(100);
        builder.Property(x => x.MaxColumn).HasMaxLength(100);
        builder.Property(x => x.DefaultValue).HasMaxLength(2000);

        builder.HasIndex(x => x.Name).IsUnique();
    }
}
