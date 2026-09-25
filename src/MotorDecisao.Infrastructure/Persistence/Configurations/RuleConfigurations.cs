using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MotorDecisao.Domain.Entities;

namespace MotorDecisao.Infrastructure.Persistence.Configurations;

/// <summary>Maps a ruleset / scorecard.</summary>
public class RulesetConfiguration : IEntityTypeConfiguration<Ruleset>
{
    public void Configure(EntityTypeBuilder<Ruleset> builder)
    {
        builder.ToTable("rulesets");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.ApprovalThreshold).HasColumnType("numeric(18,4)");

        builder.HasMany(x => x.Rules)
            .WithOne(r => r.Ruleset!)
            .HasForeignKey(r => r.RulesetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps a single rule inside a ruleset.</summary>
public class RuleConfiguration : IEntityTypeConfiguration<Rule>
{
    public void Configure(EntityTypeBuilder<Rule> builder)
    {
        builder.ToTable("rules");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ConditionExpression).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Effect).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ScoreWeight).HasColumnType("numeric(18,4)");
        builder.Property(x => x.ForcedOutcome).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Message).HasMaxLength(2000);
        builder.Property(x => x.IsEnabled).HasDefaultValue(true);

        builder.HasIndex(x => new { x.RulesetId, x.Order });
    }
}

/// <summary>Maps a declared policy input field.</summary>
public class InputFieldConfiguration : IEntityTypeConfiguration<InputField>
{
    public void Configure(EntityTypeBuilder<InputField> builder)
    {
        builder.ToTable("input_fields");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Required).HasDefaultValue(false);

        builder.HasIndex(x => new { x.FlowVersionId, x.Name }).IsUnique();

        builder.HasOne(x => x.FlowVersion)
            .WithMany(v => v.InputFields)
            .HasForeignKey(x => x.FlowVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps a reusable named formula.</summary>
public class FormulaConfiguration : IEntityTypeConfiguration<Formula>
{
    public void Configure(EntityTypeBuilder<Formula> builder)
    {
        builder.ToTable("formulas");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Expression).HasMaxLength(4000).IsRequired();

        builder.HasIndex(x => new { x.FlowVersionId, x.Key }).IsUnique();

        builder.HasOne(x => x.FlowVersion)
            .WithMany()
            .HasForeignKey(x => x.FlowVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps a global (cross-policy) reusable named formula.</summary>
public class GlobalVariableConfiguration : IEntityTypeConfiguration<GlobalVariable>
{
    public void Configure(EntityTypeBuilder<GlobalVariable> builder)
    {
        builder.ToTable("global_variables");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Expression).HasMaxLength(4000).IsRequired();

        builder.HasIndex(x => x.Key).IsUnique();
    }
}
