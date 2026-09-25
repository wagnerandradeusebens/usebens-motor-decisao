using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MotorDecisao.Domain.Entities;

namespace MotorDecisao.Infrastructure.Persistence.Configurations;

/// <summary>Mapeia o bundle congelado de uma publicação (principal + subs).</summary>
public class PublishedBundleConfiguration : IEntityTypeConfiguration<PublishedBundle>
{
    public void Configure(EntityTypeBuilder<PublishedBundle> builder)
    {
        builder.ToTable("published_bundles");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RootFlowId).IsRequired();
        builder.Property(x => x.RootFlowVersionId).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.SnapshotsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.MembersJson).HasColumnType("jsonb").IsRequired();

        // Consulta principal: bundle ativo por política.
        builder.HasIndex(x => new { x.RootFlowId, x.IsActive });
    }
}
