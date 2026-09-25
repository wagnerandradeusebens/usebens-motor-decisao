namespace MotorDecisao.Application.PublishedFlows;

/// <summary>
/// Identifies exactly which published version is current for a flow. Comparing
/// stamps is a cheap way for each running replica to detect that another replica
/// published a new version, so an in-memory cache can stay correct without a
/// distributed cache or message bus.
/// </summary>
/// <param name="FlowVersionId">The currently published version's id.</param>
/// <param name="PublishedAt">When that version was published.</param>
public readonly record struct PublishedFlowStamp(Guid FlowVersionId, DateTime PublishedAt);

/// <summary>
/// Loads published-flow data straight from the source of truth (the database).
/// The cache uses this on a miss or when a stamp check shows the cached snapshot
/// is stale. Implemented in the infrastructure layer.
/// </summary>
public interface IPublishedFlowLoader
{
    /// <summary>
    /// Returns the lightweight stamp of the flow's currently published version,
    /// or <c>null</c> if the flow has no published version.
    /// </summary>
    Task<PublishedFlowStamp?> GetStampAsync(Guid flowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the full published snapshot for a flow, or <c>null</c> if the flow
    /// has no published version.
    /// </summary>
    Task<PublishedFlowSnapshot?> LoadAsync(Guid flowId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fast, in-memory access to the published version of a flow. This is the hot
/// path the decision engine calls per proposal; it must avoid database reads of
/// rules/formulas on cache hits.
/// </summary>
public interface IPublishedFlowCache
{
    /// <summary>
    /// Returns the published snapshot for a flow, loading and caching it on first
    /// use. Returns <c>null</c> if the flow has no published version. May
    /// re-validate against the loader's stamp to pick up publishes made by other
    /// replicas.
    /// </summary>
    Task<PublishedFlowSnapshot?> GetAsync(Guid flowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the cached snapshot for a flow. Call this right after publishing a
    /// new version on this replica so the next decision reloads immediately.
    /// </summary>
    void Invalidate(Guid flowId);

    /// <summary>
    /// Drops every cached snapshot. Used when something shared across all flows
    /// changes (e.g. a global variable), since a per-flow stamp check would not
    /// detect it.
    /// </summary>
    void InvalidateAll();
}
