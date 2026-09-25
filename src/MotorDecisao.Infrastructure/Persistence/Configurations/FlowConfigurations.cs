using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MotorDecisao.Domain.Entities;

namespace MotorDecisao.Infrastructure.Persistence.Configurations;

/// <summary>Maps the <see cref="DecisionFlow"/> aggregate root.</summary>
public class DecisionFlowConfiguration : IEntityTypeConfiguration<DecisionFlow>
{
    public void Configure(EntityTypeBuilder<DecisionFlow> builder)
    {
        builder.ToTable("decision_flows");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.Name).IsUnique();

        builder.HasMany(x => x.Versions)
            .WithOne(v => v.DecisionFlow!)
            .HasForeignKey(v => v.DecisionFlowId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps a single versioned snapshot of a flow.</summary>
public class FlowVersionConfiguration : IEntityTypeConfiguration<FlowVersion>
{
    public void Configure(EntityTypeBuilder<FlowVersion> builder)
    {
        builder.ToTable("flow_versions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.VersionNumber).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ChangeLog).HasMaxLength(2000);

        // A flow cannot have two versions sharing the same number.
        builder.HasIndex(x => new { x.DecisionFlowId, x.VersionNumber }).IsUnique();

        builder.HasMany(x => x.Nodes)
            .WithOne(n => n.FlowVersion!)
            .HasForeignKey(n => n.FlowVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Edges)
            .WithOne(e => e.FlowVersion!)
            .HasForeignKey(e => e.FlowVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Rulesets)
            .WithOne(r => r.FlowVersion!)
            .HasForeignKey(r => r.FlowVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps a node on the canvas.</summary>
public class FlowNodeConfiguration : IEntityTypeConfiguration<FlowNode>
{
    public void Configure(EntityTypeBuilder<FlowNode> builder)
    {
        builder.ToTable("flow_nodes");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.NodeKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Config).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => new { x.FlowVersionId, x.NodeKey }).IsUnique();

        // A Ruleset node points at its ruleset, but deleting the node must not
        // cascade into the ruleset (it is owned by the version, not the node).
        builder.HasOne(x => x.Ruleset)
            .WithMany()
            .HasForeignKey(x => x.RulesetId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>Maps a directed edge between two nodes.</summary>
public class FlowEdgeConfiguration : IEntityTypeConfiguration<FlowEdge>
{
    public void Configure(EntityTypeBuilder<FlowEdge> builder)
    {
        builder.ToTable("flow_edges");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.EdgeKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SourceNodeKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.TargetNodeKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SourceHandle).HasMaxLength(100);
        builder.Property(x => x.Label).HasMaxLength(200);

        builder.HasIndex(x => new { x.FlowVersionId, x.EdgeKey }).IsUnique();
    }
}
