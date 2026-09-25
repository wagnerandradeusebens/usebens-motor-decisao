namespace MotorDecisao.Application.PublishedFlows;

/// <summary>
/// Tuning knobs for the published-flow cache. Defaults favour correctness across
/// multiple replicas (a short stamp re-check) while still avoiding a database
/// read of the full rule/formula set on the hot path.
/// </summary>
public sealed class PublishedFlowCacheOptions
{
    /// <summary>
    /// How long a loaded snapshot may be served before it is evicted entirely and
    /// reloaded from the database. A safety net on top of stamp re-validation.
    /// </summary>
    public TimeSpan AbsoluteTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Minimum interval between lightweight stamp checks against the database. A
    /// small value keeps replicas closely in sync after a publish; a larger value
    /// reduces database chatter. The stamp query is cheap (single row).
    /// </summary>
    public TimeSpan StampRecheckInterval { get; set; } = TimeSpan.FromSeconds(10);
}
