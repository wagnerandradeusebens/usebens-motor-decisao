namespace MotorDecisao.Application.Execution;

/// <summary>
/// Orchestrates a full decision: resolve the published (compiled) flow, run the
/// executor against the proposal, and persist the execution and its trace. This
/// is the entry point the API calls per proposal.
/// </summary>
public interface IDecisionService
{
    Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides the compiled form of a flow's published version, compiling once and
/// caching. Sits on top of the published-flow snapshot cache so the executor gets
/// pre-parsed formulas without touching the database on the hot path.
/// </summary>
public interface ICompiledFlowProvider
{
    /// <summary>
    /// Returns the compiled published flow, or <c>null</c> if the flow has no
    /// published version.
    /// </summary>
    Task<CompiledFlow?> GetAsync(Guid flowId, CancellationToken cancellationToken = default);

    /// <summary>Drops the compiled flow after a new version is published.</summary>
    void Invalidate(Guid flowId);

    /// <summary>
    /// Drops every compiled flow. Used when a change affects all flows at once
    /// (e.g. a global variable was created/updated/deleted).
    /// </summary>
    void InvalidateAll();
}

/// <summary>
/// Resolve uma política publicada pelo NOME (para a referência
/// <c>(Política;...)</c>). Retorna null quando não há política publicada com esse
/// nome. Separado de <see cref="ICompiledFlowProvider"/> (indexado por id).
/// </summary>
public interface IPolicyByNameProvider
{
    Task<CompiledFlow?> GetByNameAsync(string policyName, CancellationToken cancellationToken = default);
}
