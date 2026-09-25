using MotorDecisao.Domain.Common;

namespace MotorDecisao.Domain.Entities;

/// <summary>
/// A directed connection between two nodes on the canvas. For branching nodes
/// (such as a Condition), <see cref="SourceHandle"/> distinguishes which output
/// the edge leaves from (e.g. "true" / "false").
/// </summary>
public class FlowEdge : Entity
{
    /// <summary>Owning flow version.</summary>
    public Guid FlowVersionId { get; set; }
    public FlowVersion? FlowVersion { get; set; }

    /// <summary>Stable client identifier for the edge (mirrors editor state).</summary>
    public string EdgeKey { get; set; } = string.Empty;

    /// <summary><see cref="FlowNode.NodeKey"/> of the source node.</summary>
    public string SourceNodeKey { get; set; } = string.Empty;

    /// <summary><see cref="FlowNode.NodeKey"/> of the target node.</summary>
    public string TargetNodeKey { get; set; } = string.Empty;

    /// <summary>
    /// Which output port of the source node this edge originates from. Null for
    /// nodes with a single output. Example values: "true", "false", or a decision
    /// branch label.
    /// </summary>
    public string? SourceHandle { get; set; }

    /// <summary>Optional label rendered on the edge.</summary>
    public string? Label { get; set; }
}
