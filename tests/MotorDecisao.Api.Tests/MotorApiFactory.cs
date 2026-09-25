using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MotorDecisao.Infrastructure.Persistence;

namespace MotorDecisao.Api.Tests;

/// <summary>
/// Boots the real API but swaps the PostgreSQL DbContext for an EF Core InMemory
/// database, so integration tests exercise the endpoints, DI graph and use-case
/// services without needing a live PostgreSQL. Each factory instance gets its own
/// isolated in-memory database.
///
/// Note: InMemory does not enforce jsonb/relational specifics — these tests cover
/// behavior (create → save → publish → decide → audit), not SQL fidelity, which
/// the migration/SQL script already validated separately.
/// </summary>
public sealed class MotorApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"motor-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Disable Secrets Manager so startup uses local config (no AWS calls).
        Environment.SetEnvironmentVariable("USE_SECRETS_MANAGER", "false");
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Strip every EF Core registration (the DbContext, its options, and the
            // Npgsql provider's internal services). Registering two providers in one
            // container throws, so we remove them all before adding InMemory.
            var efDescriptors = services
                .Where(d =>
                    d.ServiceType == typeof(MotorDecisaoDbContext) ||
                    d.ServiceType == typeof(DbContextOptions<MotorDecisaoDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    (d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ?? false) ||
                    (d.ServiceType.Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) ?? false))
                .ToList();
            foreach (var d in efDescriptors)
            {
                services.Remove(d);
            }

            // Give the InMemory context its own EF internal service provider so it
            // can never collide with a lingering provider registration.
            var efServiceProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<MotorDecisaoDbContext>(options =>
                options
                    .UseInMemoryDatabase(_dbName)
                    .UseInternalServiceProvider(efServiceProvider));
        });
    }
}
