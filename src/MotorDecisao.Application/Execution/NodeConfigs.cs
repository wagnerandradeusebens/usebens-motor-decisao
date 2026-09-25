using System.Text.Json;
using System.Text.Json.Serialization;
using MotorDecisao.Domain.Enums;

namespace MotorDecisao.Application.Execution;

/// <summary>
/// Config payload for a <see cref="FlowNodeKind.Condition"/> node: a single
/// boolean formula whose result selects the outgoing edge ("true"/"false").
/// </summary>
public sealed record ConditionConfig(
    [property: JsonPropertyName("expression")] string Expression);

/// <summary>
/// Config payload for a <see cref="FlowNodeKind.Computation"/> node: an ordered
/// list of assignments. Each writes the result of an Excel-like formula into a
/// named field that later nodes can reference.
/// </summary>
public sealed record ComputationConfig(
    [property: JsonPropertyName("assignments")] IReadOnlyList<ComputationAssignment> Assignments);

public sealed record ComputationAssignment(
    [property: JsonPropertyName("targetField")] string TargetField,
    [property: JsonPropertyName("expression")] string Expression);

/// <summary>
/// Config payload for a <see cref="FlowNodeKind.Decision"/> terminal node: the
/// outcome to emit and an optional message for the audit trail.
/// </summary>
public sealed record DecisionConfig(
    [property: JsonPropertyName("outcome")] DecisionOutcome Outcome,
    [property: JsonPropertyName("message")] string? Message = null);

/// <summary>
/// Config payload for a <see cref="FlowNodeKind.DataSource"/> node. Kept minimal
/// for now: a named source plus free-form parameters. Real bureau/API integration
/// is resolved by <c>IDataSourceResolver</c> in a later phase.
/// </summary>
public sealed record DataSourceConfig(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("parameters")] Dictionary<string, string>? Parameters = null);

/// <summary>The kind of action an <see cref="Domain.Enums.FlowNodeKind.Action"/> node runs.</summary>
public enum ActionType
{
    AddPoints,          // adiciona aos pontos (scoring)
    SetPoints,          // define os pontos
    AddLimit,           // adiciona ao limite
    SetLimit,           // define o limite
    AddJustification,   // adiciona à justificativa
    SetJustification,   // define a justificativa (apaga as anteriores)
    SetOutput           // define um parâmetro de saída nomeado
}

/// <summary>
/// One action: a type plus an Excel-like <see cref="Expression"/>. For
/// <see cref="ActionType.SetOutput"/>, <see cref="Name"/> is the output name;
/// for justification actions the expression may be plain text.
/// </summary>
public sealed record ActionItem(
    [property: JsonPropertyName("type")] ActionType Type,
    [property: JsonPropertyName("expression")] string Expression,
    [property: JsonPropertyName("name")] string? Name = null);

/// <summary>Config payload for an Action node: an ordered list of actions.</summary>
public sealed record ActionsConfig(
    [property: JsonPropertyName("actions")] IReadOnlyList<ActionItem> Actions);

/// <summary>
/// Config payload for a <see cref="Domain.Enums.FlowNodeKind.Comment"/> node: a
/// free-text annotation shown on the canvas. Has no effect on execution.
/// </summary>
public sealed record CommentConfig(
    [property: JsonPropertyName("text")] string Text = "");

/// <summary>What a matched matrix cell's value does.</summary>
public enum MatrixMode
{
    Points,    // adiciona o valor da célula aos pontos
    Limit,     // adiciona o valor da célula ao limite
    Decision   // a célula contém um desfecho e encerra
}

/// <summary>
/// A numeric band on a matrix dimension. <see cref="Min"/>/<see cref="Max"/> are
/// optional (open-ended). Matching is <c>Min &lt;= v &lt; Max</c>.
/// </summary>
public sealed record MatrixBand(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("min")] decimal? Min,
    [property: JsonPropertyName("max")] decimal? Max);

/// <summary>
/// Config for a Matrix node. The two dimension expressions produce numbers; the
/// matching row/column bands select a cell from <see cref="Cells"/> (indexed
/// [row][col] as strings). For Points/Limit modes the cell is a number; for
/// Decision mode it is a <c>DecisionOutcome</c> name. <see cref="DefaultValue"/>
/// applies when a value falls outside all bands.
/// </summary>
public sealed record MatrixConfig(
    [property: JsonPropertyName("mode")] MatrixMode Mode,
    [property: JsonPropertyName("rowExpression")] string RowExpression,
    [property: JsonPropertyName("colExpression")] string ColExpression,
    [property: JsonPropertyName("rowBands")] IReadOnlyList<MatrixBand> RowBands,
    [property: JsonPropertyName("colBands")] IReadOnlyList<MatrixBand> ColBands,
    [property: JsonPropertyName("cells")] IReadOnlyList<IReadOnlyList<string>> Cells,
    [property: JsonPropertyName("defaultValue")] string? DefaultValue = null);

/// <summary>
/// Deserializes a node's <c>Config</c> jsonb string into its typed shape. Uses
/// case-insensitive, string-tolerant enum handling so the editor's JSON is easy
/// to author.
/// </summary>
public static class NodeConfig
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static T Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Configuração do nó está vazia.");
        }

        var config = JsonSerializer.Deserialize<T>(json, Options);
        if (config is null)
        {
            throw new InvalidOperationException("Configuração do nó é inválida.");
        }

        return config;
    }

    public static bool TryDeserialize<T>(string json, out T? config)
    {
        try
        {
            config = Deserialize<T>(json);
            return true;
        }
        catch
        {
            config = default;
            return false;
        }
    }
}
