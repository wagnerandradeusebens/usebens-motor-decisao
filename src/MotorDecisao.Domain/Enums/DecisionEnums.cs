namespace MotorDecisao.Domain.Enums;

/// <summary>
/// Lifecycle state of a decision flow version. Only one version of a flow may be
/// <see cref="Published"/> at a time; the engine always executes the published version.
/// </summary>
public enum FlowVersionStatus
{
    /// <summary>Editable working copy. Not executable in production.</summary>
    Draft = 0,

    /// <summary>Immutable, active version used by the engine to make decisions.</summary>
    Published = 1,

    /// <summary>Previously published version that has been superseded.</summary>
    Archived = 2
}

/// <summary>
/// The kind of a node in the visual decision flow. This mirrors the palette of
/// blocks a business user can drag onto the canvas in the editor.
/// </summary>
public enum FlowNodeKind
{
    /// <summary>Entry point of the flow. Exactly one per version.</summary>
    Start = 0,

    /// <summary>Evaluates a boolean formula and branches true/false.</summary>
    Condition = 1,

    /// <summary>Applies a ruleset (a set of rules / a scorecard).</summary>
    Ruleset = 2,

    /// <summary>Computes one or more named values from Excel-like formulas.</summary>
    Computation = 3,

    /// <summary>Calls an external data source (bureau, internal API, etc.).</summary>
    DataSource = 4,

    /// <summary>Terminal node that yields a final decision outcome.</summary>
    Decision = 5,

    /// <summary>
    /// Executes a list of actions when visited (Crivo-style): add/set points and
    /// limit, add/set justification, set named output parameters.
    /// </summary>
    Action = 6,

    /// <summary>
    /// Rule Matrix (Crivo "Regra Matriz"): crosses two dimensions (row × column)
    /// and applies the matching cell's value as points, limit, or a decision.
    /// </summary>
    Matrix = 7,

    /// <summary>
    /// Purely visual annotation on the canvas (Crivo "Comentário"). Carries no
    /// executable behaviour: it is ignored by compilation and, if reached during
    /// traversal, simply passes control to its outgoing edge.
    /// </summary>
    Comment = 8
}

/// <summary>
/// The final outcome produced by the engine for a given proposal, in the spirit
/// of a credit policy result.
/// </summary>
public enum DecisionOutcome
{
    /// <summary>No terminal outcome was reached yet.</summary>
    Pending = 0,

    /// <summary>Proposal approved without restrictions.</summary>
    Approved = 1,

    /// <summary>Approved subject to conditions (limit, rate, guarantees, etc.).</summary>
    ApprovedWithConditions = 2,

    /// <summary>Routed to a human analyst for manual review.</summary>
    ManualReview = 3,

    /// <summary>Proposal denied.</summary>
    Denied = 4
}

/// <summary>
/// How the outcome of a single rule contributes to the enclosing ruleset.
/// </summary>
public enum RuleEffect
{
    /// <summary>Adds/subtracts points to the scorecard when the rule matches.</summary>
    Score = 0,

    /// <summary>Immediately forces an outcome when the rule matches (a policy gate).</summary>
    Decision = 1,

    /// <summary>Attaches a message/flag without changing score or outcome.</summary>
    Annotation = 2
}

/// <summary>
/// Runtime status of a single decision execution.
/// </summary>
public enum ExecutionStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2
}

/// <summary>
/// The value type expected for a declared policy input field. Drives the control
/// rendered in the execution portal and how the value is coerced.
/// </summary>
public enum InputFieldType
{
    Number = 0,
    Text = 1,
    Boolean = 2,
    Date = 3
}
