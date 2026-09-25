using MotorDecisao.Api.Endpoints;
using MotorDecisao.Application.Flows;

namespace MotorDecisao.Api.Endpoints;

/// <summary>
/// CRUD de tabelas de parâmetros globais — compartilhadas entre políticas e
/// consultadas nas fórmulas por <c>PROCV("nome"; ...)</c>. Uma tabela local de
/// mesmo nome tem prioridade para aquela política.
/// </summary>
public static class GlobalParameterTableEndpoints
{
    public static IEndpointRouteBuilder MapGlobalParameterTableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/global-tables").WithTags("GlobalParameterTables");

        group.MapGet("/", async (IGlobalParameterTableService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (GlobalParameterTableInput req, IGlobalParameterTableService svc, CancellationToken ct) =>
        {
            var result = await svc.CreateAsync(req, ct);
            return result.ToHttp(t => Results.Created($"/global-tables/{t.Id}", t));
        });

        group.MapPut("/{id:guid}", async (Guid id, GlobalParameterTableInput req, IGlobalParameterTableService svc, CancellationToken ct) =>
            (await svc.UpdateAsync(id, req, ct)).ToHttp());

        group.MapDelete("/{id:guid}", async (Guid id, IGlobalParameterTableService svc, CancellationToken ct) =>
            (await svc.DeleteAsync(id, ct)).ToHttp(_ => Results.NoContent()));

        return app;
    }
}
