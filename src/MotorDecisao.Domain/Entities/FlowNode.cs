using MotorDecisao.Domain.Common;
using MotorDecisao.Domain.Enums;

namespace MotorDecisao.Domain.Entities;

/// <summary>
/// A single block on the canvas. Besides its behavioral <see cref="Kind"/>, a node
/// carries the visual position used by the drag-and-drop editor and a flexible
/// JSON <see cref="Config"/> payload whose shape depends on the kind
/// (e.g. a Condition node stores its boolean formula, a Decision node stores the
/// outcome to emit).
/// </summary>
public class FlowNode : Entity
{
    /// <summary>Owning flow version.</summary>
    public Guid FlowVersionId { get; set; }
    public FlowVersion? FlowVersion { get; set; }

    /// <summary>
    /// Stable identifier used by the front-end editor to reference the node in
    /// edges. Unique within a version. Kept separate from <see cref="Entity.Id"/>
    /// so the client can create nodes before they are persisted.
    /// </summary>
    public string NodeKey { get; set; } = string.Empty;

    /// <summary>Behavioral type of the node.</summary>
    public FlowNodeKind Kind { get; set; }

    /// <summary>Label shown on the node in the editor.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Horizontal position on the canvas (editor coordinate).</summary>
    public double PositionX { get; set; }

    /// <summary>Vertical position on the canvas (editor coordinate).</summary>
    public double PositionY { get; set; }

    /// <summary>
    /// Kind-specific configuration serialized as JSON. Stored as jsonb in
    /// PostgreSQL so it can be queried and evolves without schema changes.
    /// </summary>
    public string Config { get; set; } = "{}";

    /// <summary>Optional ruleset applied by a <see cref="FlowNodeKind.Ruleset"/> node.</summary>
    public Guid? RulesetId { get; set; }
    public Ruleset? Ruleset { get; set; }
}
