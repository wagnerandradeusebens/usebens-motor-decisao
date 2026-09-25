using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MotorDecisao.Domain.Entities;

namespace MotorDecisao.Infrastructure.Persistence.Configurations;

/// <summary>Maps a single execution of the engine for one proposal.</summary>
public class DecisionExecutionConfiguration : IEntityTypeConfiguration<DecisionExecution>
{
    public void Configure(EntityTypeBuilder<DecisionExecution> builder)
    {
        builder.ToTable("decision_executions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProposalReference).HasMaxLength(200);
        builder.Property(x => x.InputData).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Score).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Limit).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Justifications).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Outputs).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Error).HasMaxLength(4000);

        builder.HasIndex(x => x.ProposalReference);
        builder.HasIndex(x => x.CreatedAt);

        builder.HasOne(x => x.FlowVersion)
            .WithMany()
            .HasForeignKey(x => x.FlowVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Trace)
            .WithOne(t => t.DecisionExecution!)
            .HasForeignKey(t => t.DecisionExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps one audited step within an execution.</summary>
public class ExecutionTraceConfiguration : IEntityTypeConfiguration<ExecutionTrace>
{
    public void Configure(EntityTypeBuilder<ExecutionTrace> builder)
    {
        builder.ToTable("execution_traces");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.NodeKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NodeLabel).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Expression).HasMaxLength(4000);
        builder.Property(x => x.Result).HasColumnType("jsonb");
        builder.Property(x => x.Message).HasMaxLength(2000);
        builder.Property(x => x.Category).HasMaxLength(20).IsRequired().HasDefaultValue("Fluxo");
        builder.Property(x => x.Detail).HasColumnType("jsonb");

        builder.HasIndex(x => new { x.DecisionExecutionId, x.Sequence });
    }
}
