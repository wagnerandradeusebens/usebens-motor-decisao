using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotorDecisao.Api.Contracts;
using MotorDecisao.Application.Execution;
using MotorDecisao.Infrastructure.Persistence;

namespace MotorDecisao.Api.Endpoints;

/// <summary>
/// Endpoints to run decisions against a flow's published version and to read the
/// resulting executions (audit trail).
/// </summary>
public static class DecisionEndpoints
{
    public static IEndpointRouteBuilder MapDecisionEndpoints(this IEndpointRouteBuilder app)
    {
        // Run a decision.
        app.MapPost("/flows/{flowId:guid}/decisions",
            async (Guid flowId, DecisionApiRequest req, IDecisionService svc, CancellationToken ct) =>
            {
                var request = new DecisionRequest(
                    flowId,
                    req.ProposalReference,
                    FieldValueMapper.Map(req.Fields));

                try
                {
                    var result = await svc.DecideAsync(request, ct);
                    return Results.Ok(DecisionApiResponse.From(result));
                }
                catch (InvalidOperationException ex)
                {
                    // e.g. flow has no published version.
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithTags("Decisions");

        // Testar uma versão específica (rascunho) SEM publicar e SEM persistir.
        app.MapPost("/flows/{flowId:guid}/versions/{versionId:guid}/test-decision",
            async (Guid flowId, Guid versionId, DecisionApiRequest req, ITestDecisionService svc, CancellationToken ct) =>
            {
                var request = new DecisionRequest(
                    flowId,
                    req.ProposalReference,
                    FieldValueMapper.Map(req.Fields));

                try
                {
                    var result = await svc.TestAsync(flowId, versionId, request, ct);
                    return Results.Ok(DecisionApiResponse.From(result));
                }
                catch (FlowCompilationException ex)
                {
                    // Grafo/fórmula não compila: reporta como erro de validação.
                    return Results.BadRequest(new { error = ex.Message });
                }
                catch (InvalidOperationException ex)
                {
                    // Versão inexistente, etc.
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithTags("Decisions");

        // List recent executions of a flow.
        app.MapGet("/flows/{flowId:guid}/executions",
            async (Guid flowId, MotorDecisaoDbContext db, CancellationToken ct) =>
            {
                var items = await db.DecisionExecutions
                    .AsNoTracking()
                    .Where(e => db.FlowVersions
                        .Where(v => v.DecisionFlowId == flowId)
                        .Select(v => v.Id)
                        .Contains(e.FlowVersionId))
                    .OrderByDescending(e => e.CreatedAt)
                    .Take(100)
                    .Select(e => new ExecutionSummaryResponse(
                        e.Id, e.ProposalReference, e.Outcome, e.Score, e.Status, e.CreatedAt))
                    .ToListAsync(ct);

                return Results.Ok(items);
            })
            .WithTags("Decisions");

        // Get a single execution with its full trace.
        app.MapGet("/executions/{executionId:guid}",
            async (Guid executionId, MotorDecisaoDbContext db, CancellationToken ct) =>
            {
                var execution = await db.DecisionExecutions
                    .AsNoTracking()
                    .Include(e => e.Trace)
                    .FirstOrDefaultAsync(e => e.Id == executionId, ct);

                if (execution is null)
                {
                    return Results.NotFound(new { error = $"Execução {executionId} não encontrada." });
                }

                var trace = execution.Trace
                    .OrderBy(t => t.Sequence)
                    .Select(t => new TraceStepResponse(
                        t.Sequence, t.NodeKey, t.NodeLabel, t.Expression, t.Result, t.Message,
                        string.IsNullOrWhiteSpace(t.Category) ? "Fluxo" : t.Category,
                        t.PolicyName,
                        DeserializeOrEmpty<List<EvalStepResponse>>(t.Detail) ?? new()))
                    .ToList();

                var justifications = DeserializeOrEmpty<List<string>>(execution.Justifications) ?? new();
                var outputs = DeserializeOrEmpty<Dictionary<string, string>>(execution.Outputs) ?? new();

                return Results.Ok(new ExecutionDetailResponse(
                    execution.Id, execution.FlowVersionId, execution.ProposalReference,
                    execution.InputData, execution.Outcome, execution.Score, execution.Limit,
                    justifications, outputs, execution.Status,
                    execution.Error, execution.CreatedAt, execution.CompletedAt, trace));
            })
            .WithTags("Decisions");

        return app;
    }

    private static T? DeserializeOrEmpty<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch { return default; }
    }
}
