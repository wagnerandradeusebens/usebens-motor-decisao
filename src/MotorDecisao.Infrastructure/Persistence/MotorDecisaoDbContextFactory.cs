using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MotorDecisao.Infrastructure.Configuration;

namespace MotorDecisao.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the EF Core tools (<c>dotnet ef migrations</c>).
/// It builds a context without booting the whole API, reading connection details
/// from environment variables so migrations can be generated in CI or locally
/// without hard-coded credentials.
/// </summary>
public class MotorDecisaoDbContextFactory : IDesignTimeDbContextFactory<MotorDecisaoDbContext>
{
    public MotorDecisaoDbContext CreateDbContext(string[] args)
    {
        // Resolve the connection exactly like the running API does: AWS Secrets
        // Manager when enabled (USE_SECRETS_MANAGER, default true), otherwise the
        // discrete DATABASE__* env vars for local development. This keeps
        // `dotnet ef` and runtime pointed at the same database with the same
        // credentials.
        var secretsOptions = SecretsManagerOptions.FromEnvironment();

        var databaseOptions = new DatabaseOptions
        {
            ConnectionString = Environment.GetEnvironmentVariable("DATABASE__CONNECTIONSTRING"),
            Host = Environment.GetEnvironmentVariable("DATABASE__HOST") ?? "localhost",
            Port = int.TryParse(Environment.GetEnvironmentVariable("DATABASE__PORT"), out var p) ? p : 5432,
            Name = Environment.GetEnvironmentVariable("DATABASE__NAME") ?? "postgres",
            User = Environment.GetEnvironmentVariable("DATABASE__USER") ?? "postgres",
            Password = Environment.GetEnvironmentVariable("DATABASE__PASSWORD") ?? "postgres",
        };

        var connectionString = PostgresConnectionResolver.Resolve(secretsOptions, databaseOptions);

        var builder = new DbContextOptionsBuilder<MotorDecisaoDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    MotorDecisaoDbContext.Schema));

        return new MotorDecisaoDbContext(builder.Options);
    }
}
