using MotorDecisao.Domain.Common;
using MotorDecisao.Domain.Enums;

namespace MotorDecisao.Domain.Entities;

/// <summary>
/// A concrete, versioned snapshot of a <see cref="DecisionFlow"/>. The graph of
/// <see cref="Nodes"/> and <see cref="Edges"/> is exactly what the visual editor
/// renders and what the engine walks at runtime. Published versions are treated
/// as immutable so decisions remain auditable.
/// </summary>
public class FlowVersion : Entity
{
    /// <summary>Owning flow.</summary>
    public Guid DecisionFlowId { get; set; }
    public DecisionFlow? DecisionFlow { get; set; }

    /// <summary>Monotonically increasing version number within the flow (1, 2, 3...).</summary>
    public int VersionNumber { get; set; }

    /// <summary>Lifecycle state (draft / published / archived).</summary>
    public FlowVersionStatus Status { get; set; } = FlowVersionStatus.Draft;

    /// <summary>Optional note describing what changed in this version.</summary>
    public string? ChangeLog { get; set; }

    /// <summary>UTC timestamp when this version was published, if it ever was.</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>Nodes that make up the graph for this version.</summary>
    public ICollection<FlowNode> Nodes { get; set; } = new List<FlowNode>();

    /// <summary>Directed edges connecting the nodes.</summary>
    public ICollection<FlowEdge> Edges { get; set; } = new List<FlowEdge>();

    /// <summary>Rulesets available to nodes within this version.</summary>
    public ICollection<Ruleset> Rulesets { get; set; } = new List<Ruleset>();

    /// <summary>Declared input fields the proposal is expected to provide.</summary>
    public ICollection<InputField> InputFields { get; set; } = new List<InputField>();
}
