using MotorDecisao.Application.Sources;

namespace MotorDecisao.Api.Endpoints;

/// <summary>
/// Read-only catalog of registered external sources (integrations). The list
/// grows as real integrations are implemented; users cannot create sources here.
/// Consumed by the editor's formula autocomplete and the "Fontes" menu.
/// </summary>
public static class SourceEndpoints
{
    public static IEndpointRouteBuilder MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sources", (ISourceCatalog catalog) =>
        {
            var items = catalog.List().Select(s => new
            {
                name = s.Name,
                description = s.Description,
                products = s.Products.Select(p => new
                {
                    name = p.Name,
                    description = p.Description,
                    keyField = p.KeyField,
                    data = p.Data.Select(d => new { name = d.Name, description = d.Description })
                })
            });
            return Results.Ok(items);
        }).WithTags("Sources");

        // Parâmetros operacionais de uma fonte (tentativas, timeout, TTL do cache).
        app.MapGet("/sources/{name}/config", async (string name, ISourceConfigService svc, CancellationToken ct) =>
        {
            var p = await svc.GetAsync(name, ct);
            return Results.Ok(new { maxAttempts = p.MaxAttempts, timeoutSeconds = p.TimeoutSeconds, cacheTtlHours = p.CacheTtlHours });
        }).WithTags("Sources");

        app.MapPut("/sources/{name}/config", async (string name, SourceConfigDto dto, ISourceConfigService svc, CancellationToken ct) =>
        {
            var p = await svc.UpsertAsync(
                name,
                new SourceParameters(dto.MaxAttempts, dto.TimeoutSeconds, dto.CacheTtlHours),
                ct);
            return Results.Ok(new { maxAttempts = p.MaxAttempts, timeoutSeconds = p.TimeoutSeconds, cacheTtlHours = p.CacheTtlHours });
        }).WithTags("Sources");

        return app;
    }

    /// <summary>Corpo do PUT de configuração de fonte.</summary>
    public sealed record SourceConfigDto(int MaxAttempts, int TimeoutSeconds, int CacheTtlHours);
}
