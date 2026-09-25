using MotorDecisao.Api.Contracts;
using MotorDecisao.Application.Flows;

namespace MotorDecisao.Api.Endpoints;

/// <summary>
/// Endpoints for authoring flows and their versions: create/list/get flows, load
/// and save the editor graph, create new versions, and publish.
/// </summary>
public static class FlowEndpoints
{
    public static IEndpointRouteBuilder MapFlowEndpoints(this IEndpointRouteBuilder app)
    {
        var flows = app.MapGroup("/flows").WithTags("Flows");

        flows.MapPost("/", async (CreateFlowRequest req, IFlowManagementService svc, CancellationToken ct) =>
        {
            var result = await svc.CreateFlowAsync(new CreateFlowInput(req.Name, req.Description), ct);
            return result.ToHttp(flow => Results.Created($"/flows/{flow.Id}", flow));
        });

        flows.MapGet("/", async (IFlowManagementService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListFlowsAsync(ct)));

        flows.MapGet("/{flowId:guid}", async (Guid flowId, IFlowManagementService svc, CancellationToken ct) =>
            (await svc.GetFlowAsync(flowId, ct)).ToHttp());

        flows.MapDelete("/{flowId:guid}", async (Guid flowId, IFlowManagementService svc, CancellationToken ct) =>
            (await svc.DeleteFlowAsync(flowId, ct)).ToHttp(_ => Results.NoContent()));

        // Administrativo: gera o bundle congelado para políticas já publicadas que
        // ainda não têm bundle (retrocompatibilidade com o modelo de congelamento).
        // Idempotente.
        flows.MapPost("/backfill-bundles", async (IFlowManagementService svc, CancellationToken ct) =>
            (await svc.BackfillBundlesAsync(ct)).ToHttp(count => Results.Ok(new { generated = count })));

        // --- Versions ---
        var versions = flows.MapGroup("/{flowId:guid}/versions");

        versions.MapPost("/", async (Guid flowId, CreateVersionRequest? req, IFlowManagementService svc, CancellationToken ct) =>
        {
            var result = await svc.CreateVersionAsync(flowId, req?.CopyFromVersionId, ct);
            return result.ToHttp(v => Results.Created($"/flows/{flowId}/versions/{v.Id}", v));
        });

        versions.MapGet("/{versionId:guid}", async (Guid flowId, Guid versionId, IFlowManagementService svc, CancellationToken ct) =>
            (await svc.GetVersionGraphAsync(flowId, versionId, ct)).ToHttp());

        versions.MapPut("/{versionId:guid}", async (Guid flowId, Guid versionId, VersionGraph graph, IFlowManagementService svc, CancellationToken ct) =>
            (await svc.SaveVersionGraphAsync(flowId, versionId, graph, ct)).ToHttp());

        versions.MapPost("/{versionId:guid}/publish", async (Guid flowId, Guid versionId, IFlowManagementService svc, CancellationToken ct) =>
            (await svc.PublishVersionAsync(flowId, versionId, ct)).ToHttp());

        versions.MapDelete("/{versionId:guid}", async (Guid flowId, Guid versionId, IFlowManagementService svc, CancellationToken ct) =>
            (await svc.DeleteVersionAsync(flowId, versionId, ct)).ToHttp(_ => Results.NoContent()));

        return app;
    }
}
