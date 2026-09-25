using MotorDecisao.Api.Contracts;
using MotorDecisao.Application.Flows;

namespace MotorDecisao.Api.Endpoints;

/// <summary>
/// CRUD endpoints for global variables — reusable formulas shared across every
/// policy and referenced as <c>{Key}</c>. A flow's own local variable with the
/// same key wins for that flow.
/// </summary>
public static class GlobalVariableEndpoints
{
    public static IEndpointRouteBuilder MapGlobalVariableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/global-variables").WithTags("GlobalVariables");

        group.MapGet("/", async (IGlobalVariableService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (GlobalVariableRequest req, IGlobalVariableService svc, CancellationToken ct) =>
        {
            var result = await svc.CreateAsync(new GlobalVariableInput(req.Key, req.Label, req.Expression), ct);
            return result.ToHttp(v => Results.Created($"/global-variables/{v.Id}", v));
        });

        group.MapPut("/{id:guid}", async (Guid id, GlobalVariableRequest req, IGlobalVariableService svc, CancellationToken ct) =>
            (await svc.UpdateAsync(id, new GlobalVariableInput(req.Key, req.Label, req.Expression), ct)).ToHttp());

        group.MapDelete("/{id:guid}", async (Guid id, IGlobalVariableService svc, CancellationToken ct) =>
            (await svc.DeleteAsync(id, ct)).ToHttp(_ => Results.NoContent()));

        return app;
    }
}
