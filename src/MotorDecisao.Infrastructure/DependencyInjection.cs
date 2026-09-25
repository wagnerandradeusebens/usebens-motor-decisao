using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MotorDecisao.Application.Execution;
using MotorDecisao.Application.Flows;
using MotorDecisao.Application.PublishedFlows;
using MotorDecisao.Application.Sources;
using MotorDecisao.Infrastructure.Configuration;
using MotorDecisao.Infrastructure.Execution;
using MotorDecisao.Infrastructure.Flows;
using MotorDecisao.Infrastructure.Persistence;
using MotorDecisao.Infrastructure.PublishedFlows;
using MotorDecisao.Infrastructure.Sources;
using StackExchange.Redis;

namespace MotorDecisao.Infrastructure;

/// <summary>
/// Registration entry point for the infrastructure layer. Keeping the wiring here
/// lets the API host stay thin and unaware of EF Core / Npgsql details.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Local (.env) database parts, used when Secrets Manager is disabled.
        var dbOptions = new DatabaseOptions();
        configuration.GetSection(DatabaseOptions.SectionName).Bind(dbOptions);
        services.AddSingleton(dbOptions);

        // Credential strategy: AWS Secrets Manager in production (IRSA), .env
        // locally. Resolved once at startup.
        var secretsOptions = SecretsManagerOptions.FromEnvironment();
        services.AddSingleton(secretsOptions);

        var connectionString = PostgresConnectionResolver.Resolve(secretsOptions, dbOptions);

        services.AddDbContext<MotorDecisaoDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    MotorDecisaoDbContext.Schema)));

        // Published-flow cache: keeps the published version compiled/loaded so
        // decisions don't read rules from the DB on the hot path.
        // Scoped EF loader (DB access happens in a fresh scope).
        services.AddMemoryCache();
        services.AddSingleton(new PublishedFlowCacheOptions());
        services.AddScoped<IPublishedFlowLoader, EfPublishedFlowLoader>();

        // Cache backend: Redis when enabled (shared across replicas), otherwise
        // in-memory. If Redis is enabled but the server is momentarily down, the
        // Redis cache degrades to a direct DB load — and with abortConnect=false
        // the connection self-heals — so the app still starts and serves.
        var redisOptions = RedisOptions.FromEnvironment();
        if (redisOptions.Enabled)
        {
            services.AddSingleton(redisOptions);
            services.AddSingleton<IConnectionMultiplexer>(
                _ => ConnectionMultiplexer.Connect(redisOptions.ResolveConnectionString()));
            services.AddSingleton<IPublishedFlowCache, RedisPublishedFlowCache>();
        }
        else
        {
            services.AddSingleton<IPublishedFlowCache, InMemoryPublishedFlowCache>();
        }

        // Execution engine. The compiled-flow provider and executor are stateless
        // singletons; the decision service is scoped because it persists via the
        // scoped DbContext.
        services.AddSingleton<ICompiledFlowProvider, CompiledFlowProvider>();
        services.AddSingleton<IDataSourceResolver, NoOpDataSourceResolver>();

        // External data sources (integrations self-register). Fake SERASA + BACEN.
        services.AddSingleton<IExternalSource, SerasaFakeSource>();
        services.AddSingleton<IExternalSource, BacenFakeSource>();
        // Cache persistente de fontes (por fonte;produto;dado;CPF), compartilhado
        // entre políticas/execuções. Singleton que abre escopo por operação.
        services.AddSingleton<ISourceCache, SourceCache>();
        services.AddSingleton<ISourceConfigProvider, SourceConfigProvider>();
        services.AddSingleton<ISourceCatalog, SourceCatalog>();

        services.AddSingleton(sp => new FlowExecutor(
            sp.GetRequiredService<IDataSourceResolver>(),
            sp.GetRequiredService<ISourceCatalog>())
        {
            // Habilita a referência (Política;...): o provider resolve a política
            // alvo por nome e o executor a executa sob demanda (reentrância).
            PolicyProvider = sp.GetRequiredService<ICompiledFlowProvider>(),
        });
        services.AddScoped<IDecisionService, DecisionService>();

        // Flow authoring / publishing use cases (scoped: uses the DbContext).
        services.AddScoped<IFlowManagementService, FlowManagementService>();
        services.AddScoped<IGlobalVariableService, GlobalVariableService>();
        services.AddScoped<ISourceConfigService, SourceConfigService>();

        return services;
    }
}
