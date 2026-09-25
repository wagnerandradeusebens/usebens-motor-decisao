using Microsoft.EntityFrameworkCore;
using MotorDecisao.Domain.Common;
using MotorDecisao.Domain.Entities;

namespace MotorDecisao.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the decision engine. Every table lives in the dedicated
/// PostgreSQL schema <see cref="Schema"/> so the engine can share a database
/// instance with other products without colliding with their tables.
/// </summary>
public class MotorDecisaoDbContext : DbContext
{
    /// <summary>Dedicated database schema owned by this application.</summary>
    public const string Schema = "usebens_motor_decisao";

    public MotorDecisaoDbContext(DbContextOptions<MotorDecisaoDbContext> options)
        : base(options)
    {
    }

    public DbSet<DecisionFlow> DecisionFlows => Set<DecisionFlow>();
    public DbSet<FlowVersion> FlowVersions => Set<FlowVersion>();
    public DbSet<FlowNode> FlowNodes => Set<FlowNode>();
    public DbSet<FlowEdge> FlowEdges => Set<FlowEdge>();
    public DbSet<Ruleset> Rulesets => Set<Ruleset>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<Formula> Formulas => Set<Formula>();
    public DbSet<GlobalVariable> GlobalVariables => Set<GlobalVariable>();
    public DbSet<InputField> InputFields => Set<InputField>();
    public DbSet<DecisionExecution> DecisionExecutions => Set<DecisionExecution>();
    public DbSet<ExecutionTrace> ExecutionTraces => Set<ExecutionTrace>();
    public DbSet<SourceCacheEntry> SourceCacheEntries => Set<SourceCacheEntry>();
    public DbSet<SourceConfig> SourceConfigs => Set<SourceConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Pin every entity to the application's own schema.
        modelBuilder.HasDefaultSchema(Schema);

        // Apply all IEntityTypeConfiguration<T> in this assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MotorDecisaoDbContext).Assembly);
    }

    /// <summary>
    /// Keeps <see cref="Entity.UpdatedAt"/> current on every modification without
    /// requiring callers to remember to set it.
    /// </summary>
    public override int SaveChanges()
    {
        StampTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampTimestamps()
    {
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
