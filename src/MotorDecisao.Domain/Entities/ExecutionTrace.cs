using MotorDecisao.Domain.Common;

namespace MotorDecisao.Domain.Entities;

/// <summary>
/// A single audited step within a <see cref="DecisionExecution"/>: which node was
/// visited, what was evaluated, and the result. The engine appends one entry per
/// node (and per rule that fires) so the decision can be explained after the fact.
/// </summary>
public class ExecutionTrace : Entity
{
    /// <summary>Owning execution.</summary>
    public Guid DecisionExecutionId { get; set; }
    public DecisionExecution? DecisionExecution { get; set; }

    /// <summary>Zero-based sequence of the step within the execution.</summary>
    public int Sequence { get; set; }

    /// <summary><see cref="FlowNode.NodeKey"/> of the visited node.</summary>
    public string NodeKey { get; set; } = string.Empty;

    /// <summary>Label of the visited node, captured for readability.</summary>
    public string NodeLabel { get; set; } = string.Empty;

    /// <summary>Expression evaluated at this step, if any.</summary>
    public string? Expression { get; set; }

    /// <summary>Serialized result of the step (value, matched rules, branch taken).</summary>
    public string? Result { get; set; }

    /// <summary>Free-text explanation appended for the audit trail.</summary>
    public string? Message { get; set; }

    /// <summary>
    /// Block/category the step belongs to (Fonte, Variavel, Regra, Acao, Matriz...),
    /// used to group the trace when it is displayed.
    /// </summary>
    public string Category { get; set; } = "Fluxo";

    /// <summary>
    /// Nome da política a que o passo pertence (a principal ou uma subpolítica
    /// referenciada), para agrupar a trilha por política. <c>null</c> em traces
    /// antigos, anteriores a este campo.
    /// </summary>
    public string? PolicyName { get; set; }

    /// <summary>
    /// Serialized deep resolution tree of the step's formula (a JSON array of
    /// {depth, expression, value} entries), when applicable.
    /// </summary>
    public string? Detail { get; set; }
}
