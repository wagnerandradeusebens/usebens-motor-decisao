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
                    data = p.Data.Select(d => new { name = d.Name, description = d.Description })
                })
            });
            return Results.Ok(items);
        }).WithTags("Sources");

        return app;
    }
}
