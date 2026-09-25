using MotorDecisao.Application.Formulas;
using MotorDecisao.Domain.Enums;

namespace MotorDecisao.Application.Execution;

/// <summary>
/// Input to a decision run: which flow, an optional reference to correlate with
/// the calling system, and the proposal's field values.
/// </summary>
public sealed record DecisionRequest(
    Guid FlowId,
    string? ProposalReference,
    IReadOnlyDictionary<string, FormulaValue> Input);

/// <summary>Block/category a trace step belongs to, for grouping in the log.</summary>
public enum TraceCategory
{
    Fonte,
    Variavel,
    Inicio,
    Fluxo,
    Regra,
    Acao,
    Matriz,
    Decisao
}

/// <summary>One recorded step of an execution, mirroring the persisted trace.</summary>
public sealed record TraceStep(
    int Sequence,
    string NodeKey,
    string NodeLabel,
    string? Expression,
    string? Result,
    string? Message)
{
    /// <summary>Block this step belongs to (for grouping the log).</summary>
    public TraceCategory Category { get; init; } = TraceCategory.Fluxo;

    /// <summary>
    /// Nome da política a que este passo pertence — a principal ou uma
    /// subpolítica referenciada. Permite agrupar a trilha por política sem
    /// depender de prefixos no rótulo do nó. <c>null</c> para passos legados.
    /// </summary>
    public string? PolicyName { get; init; }

    /// <summary>Deep resolution tree of the step's formula, when applicable.</summary>
    public IReadOnlyList<EvalStep> Detail { get; init; } = Array.Empty<EvalStep>();
}

/// <summary>
/// Outcome of a decision run: the final outcome, the accumulated scorecard total,
/// and the full ordered trace. When persisted, the execution id links back to the
/// stored record.
/// </summary>
public sealed record DecisionResult(
    Guid FlowId,
    Guid FlowVersionId,
    DecisionOutcome Outcome,
    decimal Score,
    ExecutionStatus Status,
    string? Error,
    IReadOnlyList<TraceStep> Trace)
{
    public Guid? ExecutionId { get; init; }

    /// <summary>Accumulated credit limit counter (Crivo "Limite").</summary>
    public decimal Limit { get; init; }

    /// <summary>Ordered justification messages composed during the run.</summary>
    public IReadOnlyList<string> Justifications { get; init; } = Array.Empty<string>();

    /// <summary>Named output parameters set during the run (limite, taxa, etc.).</summary>
    public IReadOnlyDictionary<string, string> Outputs { get; init; } =
        new Dictionary<string, string>();

    /// <summary>A "resposta" (string) definida pela política durante a execução.</summary>
    public string Resposta { get; init; } = string.Empty;

    /// <summary>
    /// As variáveis avaliadas durante a execução (nome → valor). Usadas para
    /// resolver <c>(Política;Variaveis;nome)</c> a partir de outra política.
    /// </summary>
    public IReadOnlyDictionary<string, Formulas.FormulaValue> Variables { get; init; } =
        new Dictionary<string, Formulas.FormulaValue>();
}
