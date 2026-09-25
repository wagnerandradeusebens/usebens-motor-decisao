using MotorDecisao.Domain.Enums;

namespace MotorDecisao.Application.Flows;

/// <summary>
/// The full editable graph of a flow version, as the visual editor sees it. This
/// is what <c>GET</c> returns and what <c>PUT</c> saves for a Draft version.
/// </summary>
public sealed record VersionGraph(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<GraphRuleset> Rulesets,
    IReadOnlyList<GraphFormula> Formulas,
    IReadOnlyList<GraphInputField> InputFields)
{
    /// <summary>
    /// Tabelas de parâmetros LOCAIS da versão (salvas/carregadas junto com o
    /// grafo, como as fórmulas; congelam na publicação). Opcional para
    /// compatibilidade com payloads antigos.
    /// </summary>
    public IReadOnlyList<GraphTable> Tables { get; init; } = Array.Empty<GraphTable>();

    /// <summary>
    /// Avisos de validação preenchidos na RESPOSTA do save (fórmulas inválidas,
    /// PROCV em tabela sem coluna-chave, etc.). Não bloqueiam o salvamento — o
    /// editor os exibe ao usuário. Vazio no GET.
    /// </summary>
    public IReadOnlyList<ValidationWarning> Warnings { get; init; } = Array.Empty<ValidationWarning>();
}

/// <summary>
/// Uma tabela de parâmetros no grafo editável. Colunas e linhas viajam já
/// estruturadas (o backend serializa para jsonb ao persistir).
/// </summary>
public sealed record GraphTable(
    string Name,
    string Label,
    IReadOnlyList<GraphTableColumn> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string? KeyColumn,
    string? MinColumn,
    string? MaxColumn,
    string? DefaultValue);

/// <summary>Coluna de uma tabela de parâmetros: nome + tipo.</summary>
public sealed record GraphTableColumn(string Name, string Type);

/// <summary>A declared input field of the policy (drives the portal + autocomplete).</summary>
public sealed record GraphInputField(
    string Name,
    string Label,
    InputFieldType Type,
    bool Required,
    int Order);

public sealed record GraphNode(
    string NodeKey,
    FlowNodeKind Kind,
    string Label,
    double PositionX,
    double PositionY,
    string Config,
    string? RulesetKey);

public sealed record GraphEdge(
    string EdgeKey,
    string SourceNodeKey,
    string TargetNodeKey,
    string? SourceHandle,
    string? Label);

/// <summary>
/// A ruleset in the editable graph. Uses a client-side <see cref="RulesetKey"/>
/// so nodes can reference rulesets before persistence assigns real ids.
/// </summary>
public sealed record GraphRuleset(
    string RulesetKey,
    string Name,
    string? Description,
    decimal? ApprovalThreshold,
    IReadOnlyList<GraphRule> Rules);

public sealed record GraphRule(
    int Order,
    string Name,
    string ConditionExpression,
    RuleEffect Effect,
    decimal ScoreWeight,
    DecisionOutcome? ForcedOutcome,
    string? Message,
    bool IsEnabled);

public sealed record GraphFormula(
    string Key,
    string Label,
    string Expression);
