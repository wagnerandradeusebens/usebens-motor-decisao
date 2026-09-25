using MotorDecisao.Domain.Common;
using MotorDecisao.Domain.Enums;

namespace MotorDecisao.Domain.Entities;

/// <summary>
/// One run of the engine for a single proposal against a specific
/// <see cref="FlowVersion"/>. It records the exact input, the final outcome and a
/// full <see cref="Trace"/> so every decision is reproducible and auditable.
/// </summary>
public class DecisionExecution : Entity
{
    /// <summary>Flow version that was executed (the published one, in production).</summary>
    public Guid FlowVersionId { get; set; }
    public FlowVersion? FlowVersion { get; set; }

    /// <summary>
    /// Caller-supplied reference for the proposal (e.g. an application id) so the
    /// execution can be correlated with the originating system.
    /// </summary>
    public string? ProposalReference { get; set; }

    /// <summary>The proposal payload the decision was based on, stored as JSON.</summary>
    public string InputData { get; set; } = "{}";

    /// <summary>Runtime status of the execution.</summary>
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Running;

    /// <summary>Final outcome produced by the flow.</summary>
    public DecisionOutcome Outcome { get; set; } = DecisionOutcome.Pending;

    /// <summary>Total scorecard points accumulated during the run.</summary>
    public decimal Score { get; set; }

    /// <summary>Accumulated credit limit counter.</summary>
    public decimal Limit { get; set; }

    /// <summary>Justification messages composed during the run, as a JSON array.</summary>
    public string Justifications { get; set; } = "[]";

    /// <summary>Named output parameters, as a JSON object.</summary>
    public string Outputs { get; set; } = "{}";

    /// <summary>Error message if <see cref="Status"/> is <see cref="ExecutionStatus.Failed"/>.</summary>
    public string? Error { get; set; }

    /// <summary>UTC timestamp when the run finished (success or failure).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Ordered trace of every step the engine took.</summary>
    public ICollection<ExecutionTrace> Trace { get; set; } = new List<ExecutionTrace>();
}
