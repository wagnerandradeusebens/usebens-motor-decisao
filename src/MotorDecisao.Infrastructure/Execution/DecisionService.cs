using System.Text.Json;
using MotorDecisao.Application.Execution;
using MotorDecisao.Domain.Entities;
using MotorDecisao.Domain.Enums;
using MotorDecisao.Infrastructure.Persistence;

namespace MotorDecisao.Infrastructure.Execution;

/// <summary>
/// Runs a decision end-to-end: fetches the compiled published flow (from cache),
/// executes it against the proposal, and persists the <see cref="DecisionExecution"/>
/// with its full trace in a single write. Reads of rules/formulas never touch the
/// database on the hot path; the only DB round-trip is this final save.
/// </summary>
public sealed class DecisionService : IDecisionService
{
    private readonly ICompiledFlowProvider _flows;
    private readonly FlowExecutor _executor;
    private readonly MotorDecisaoDbContext _db;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public DecisionService(
        ICompiledFlowProvider flows,
        FlowExecutor executor,
        MotorDecisaoDbContext db)
    {
        _flows = flows;
        _executor = executor;
        _db = db;
    }

    public async Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        var flow = await _flows.GetAsync(request.FlowId, cancellationToken);
        if (flow is null)
        {
            throw new InvalidOperationException(
                $"Fluxo {request.FlowId} não possui versão publicada.");
        }

        var result = await _executor.ExecuteAsync(flow, request, cancellationToken);

        var execution = new DecisionExecution
        {
            FlowVersionId = result.FlowVersionId,
            ProposalReference = request.ProposalReference,
            InputData = SerializeInput(request),
            Status = result.Status,
            Outcome = result.Outcome,
            Score = result.Score,
            Limit = result.Limit,
            Justifications = JsonSerializer.Serialize(result.Justifications, JsonOptions),
            Outputs = JsonSerializer.Serialize(result.Outputs, JsonOptions),
            Error = result.Error,
            CompletedAt = DateTime.UtcNow
        };

        foreach (var step in result.Trace)
        {
            execution.Trace.Add(new ExecutionTrace
            {
                Sequence = step.Sequence,
                NodeKey = step.NodeKey,
                NodeLabel = step.NodeLabel,
                Expression = step.Expression,
                Result = step.Result is null ? null : JsonSerializer.Serialize(step.Result, JsonOptions),
                Message = step.Message,
                Category = step.Category.ToString(),
                Detail = step.Detail.Count == 0 ? null : JsonSerializer.Serialize(step.Detail, JsonOptions)
            });
        }

        _db.DecisionExecutions.Add(execution);
        await _db.SaveChangesAsync(cancellationToken);

        return result with { ExecutionId = execution.Id };
    }

    private static string SerializeInput(DecisionRequest request)
    {
        // Store the input as a simple string map so the decision is reproducible.
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in request.Input)
        {
            map[kv.Key] = kv.Value.ToString();
        }
        return JsonSerializer.Serialize(map, JsonOptions);
    }
}
