using MotorDecisao.Domain.Enums;

namespace MotorDecisao.Application.PublishedFlows;

/// <summary>
/// Immutable, in-memory representation of the published version of a decision
/// flow. This is what the engine holds in cache and evaluates against a proposal,
/// so decisions never hit the database to read rules/formulas at runtime.
///
/// For now it is a faithful read-only copy of the persisted graph. When the
/// formula/execution engine lands, the compiled artifacts (parsed formulas,
/// prepared rule predicates) will be attached here without changing the cache
/// contract.
/// </summary>
public sealed record PublishedFlowSnapshot(
    Guid FlowId,
    string FlowName,
    Guid FlowVersionId,
    int VersionNumber,
    DateTime PublishedAt,
    IReadOnlyList<PublishedNode> Nodes,
    IReadOnlyList<PublishedEdge> Edges,
    IReadOnlyList<PublishedRuleset> Rulesets,
    IReadOnlyList<PublishedFormula> Formulas)
{
    /// <summary>
    /// Tabelas de parâmetros disponíveis ao fluxo (locais da versão + globais
    /// mescladas; local vence). Congeladas junto na publicação. Opcional para
    /// compatibilidade com snapshots antigos (default vazio).
    /// </summary>
    public IReadOnlyList<PublishedTable> Tables { get; init; } = Array.Empty<PublishedTable>();
}

/// <summary>
/// Uma tabela de parâmetros congelada: colunas, linhas e os metadados de consulta
/// (coluna-chave para <c>PROCV</c>, colunas min/max para <c>PROCV.FAIXA</c>) e o
/// valor padrão. Colunas e linhas já vêm desserializadas do jsonb.
/// </summary>
public sealed record PublishedTable(
    string Name,
    IReadOnlyList<PublishedTableColumn> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string? KeyColumn,
    string? MinColumn,
    string? MaxColumn,
    string? DefaultValue);

/// <summary>Uma coluna de uma tabela de parâmetros: nome + tipo.</summary>
public sealed record PublishedTableColumn(string Name, string Type);

/// <summary>A node on the published graph.</summary>
public sealed record PublishedNode(
    string NodeKey,
    FlowNodeKind Kind,
    string Label,
    string Config,
    Guid? RulesetId);

/// <summary>A directed connection between two published nodes.</summary>
public sealed record PublishedEdge(
    string EdgeKey,
    string SourceNodeKey,
    string TargetNodeKey,
    string? SourceHandle,
    string? Label);

/// <summary>A ruleset (scorecard) and its ordered rules.</summary>
public sealed record PublishedRuleset(
    Guid RulesetId,
    string Name,
    decimal? ApprovalThreshold,
    IReadOnlyList<PublishedRule> Rules);

/// <summary>A single rule within a published ruleset.</summary>
public sealed record PublishedRule(
    Guid RuleId,
    int Order,
    string Name,
    string ConditionExpression,
    RuleEffect Effect,
    decimal ScoreWeight,
    DecisionOutcome? ForcedOutcome,
    string? Message);

/// <summary>A reusable named formula available to the flow.</summary>
public sealed record PublishedFormula(
    string Key,
    string Label,
    string Expression);
