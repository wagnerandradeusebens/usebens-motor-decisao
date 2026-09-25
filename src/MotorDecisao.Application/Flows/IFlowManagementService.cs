using MotorDecisao.Application.Common;

namespace MotorDecisao.Application.Flows;

/// <summary>
/// Use cases for authoring and publishing decision flows. The API layer calls
/// these and maps <see cref="OperationResult{T}"/> failures to HTTP status codes.
/// </summary>
public interface IFlowManagementService
{
    /// <summary>Creates a flow with an empty version 1 in Draft.</summary>
    Task<OperationResult<FlowSummary>> CreateFlowAsync(CreateFlowInput input, CancellationToken ct = default);

    /// <summary>Lists all flows with their version summaries.</summary>
    Task<IReadOnlyList<FlowSummary>> ListFlowsAsync(CancellationToken ct = default);

    /// <summary>Gets a single flow with its version summaries.</summary>
    Task<OperationResult<FlowSummary>> GetFlowAsync(Guid flowId, CancellationToken ct = default);

    /// <summary>Loads the editable graph of a specific version.</summary>
    Task<OperationResult<VersionGraph>> GetVersionGraphAsync(Guid flowId, Guid versionId, CancellationToken ct = default);

    /// <summary>
    /// Replaces the graph of a Draft version. Rejected with a conflict if the
    /// version is not a Draft (published versions are immutable).
    /// </summary>
    Task<OperationResult<VersionGraph>> SaveVersionGraphAsync(Guid flowId, Guid versionId, VersionGraph graph, CancellationToken ct = default);

    /// <summary>
    /// Creates a new Draft version, optionally copying the graph of an existing
    /// version (e.g. to iterate on the currently published one).
    /// </summary>
    Task<OperationResult<FlowVersionSummary>> CreateVersionAsync(Guid flowId, Guid? copyFromVersionId, CancellationToken ct = default);

    /// <summary>
    /// Validates and publishes a Draft version: the graph must compile (exactly
    /// one Start, all formulas valid). On success the previously published version
    /// is archived and the compiled-flow cache is invalidated.
    /// </summary>
    Task<OperationResult<FlowVersionSummary>> PublishVersionAsync(Guid flowId, Guid versionId, CancellationToken ct = default);

    /// <summary>
    /// Deletes a flow and everything under it (versions, graph, rulesets, formulas,
    /// input fields) along with its decision executions and traces. Invalidates the
    /// compiled-flow cache. This is destructive and irreversible.
    /// </summary>
    Task<OperationResult<bool>> DeleteFlowAsync(Guid flowId, CancellationToken ct = default);
}
