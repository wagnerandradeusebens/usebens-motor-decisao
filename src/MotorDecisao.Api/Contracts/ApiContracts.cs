using System.Text.Json;
using MotorDecisao.Application.Execution;
using MotorDecisao.Domain.Enums;

namespace MotorDecisao.Api.Contracts;

// --- Requests ---------------------------------------------------------------

/// <summary>Body to create a flow.</summary>
public sealed record CreateFlowRequest(string Name, string? Description);

/// <summary>Body to create a new version, optionally copying an existing one.</summary>
public sealed record CreateVersionRequest(Guid? CopyFromVersionId);

/// <summary>Body to create or update a global variable.</summary>
public sealed record GlobalVariableRequest(string Key, string Label, string Expression);

/// <summary>
/// Body to run a decision. <see cref="Fields"/> is a free-form map of proposal
/// values (numbers, strings, booleans, ISO dates).
/// </summary>
public sealed record DecisionApiRequest(
    string? ProposalReference,
    IReadOnlyDictionary<string, JsonElement>? Fields);

// --- Responses --------------------------------------------------------------

public sealed record DecisionApiResponse(
    Guid? ExecutionId,
    Guid FlowId,
    Guid FlowVersionId,
    DecisionOutcome Outcome,
    decimal Score,
    decimal Limit,
    IReadOnlyList<string> Justifications,
    IReadOnlyDictionary<string, string> Outputs,
    ExecutionStatus Status,
    string? Error,
    IReadOnlyList<TraceStepResponse> Trace)
{
    public static DecisionApiResponse From(DecisionResult r) => new(
        r.ExecutionId, r.FlowId, r.FlowVersionId, r.Outcome, r.Score, r.Limit,
        r.Justifications, r.Outputs, r.Status, r.Error,
        r.Trace.Select(t => new TraceStepResponse(
            t.Sequence, t.NodeKey, t.NodeLabel, t.Expression, t.Result, t.Message,
            t.Category.ToString(),
            t.Detail.Select(d => new EvalStepResponse(d.Depth, d.Expression, d.Value)).ToList())).ToList());
}

/// <summary>One node in the deep resolution tree of a formula.</summary>
public sealed record EvalStepResponse(int Depth, string Expression, string Value);

public sealed record TraceStepResponse(
    int Sequence,
    string NodeKey,
    string NodeLabel,
    string? Expression,
    string? Result,
    string? Message,
    string Category,
    IReadOnlyList<EvalStepResponse> Detail);

public sealed record ExecutionSummaryResponse(
    Guid Id,
    string? ProposalReference,
    DecisionOutcome Outcome,
    decimal Score,
    ExecutionStatus Status,
    DateTime CreatedAt);

public sealed record ExecutionDetailResponse(
    Guid Id,
    Guid FlowVersionId,
    string? ProposalReference,
    string InputData,
    DecisionOutcome Outcome,
    decimal Score,
    decimal Limit,
    IReadOnlyList<string> Justifications,
    IReadOnlyDictionary<string, string> Outputs,
    ExecutionStatus Status,
    string? Error,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    IReadOnlyList<TraceStepResponse> Trace);
