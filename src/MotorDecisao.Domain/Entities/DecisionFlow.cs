using MotorDecisao.Domain.Common;

namespace MotorDecisao.Domain.Entities;

/// <summary>
/// A decision flow is the top-level, named policy that a business user builds in
/// the visual editor (e.g. "Auto loan - individuals"). It is a container for one
/// or more <see cref="FlowVersion"/> records; versioning lets policies evolve
/// while keeping every past decision reproducible.
/// </summary>
public class DecisionFlow : Entity
{
    /// <summary>Human-readable, unique name of the policy.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional free-text description of what the policy does.</summary>
    public string? Description { get; set; }

    /// <summary>Whether the flow is available for execution at all.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>All versions belonging to this flow, across their lifecycle.</summary>
    public ICollection<FlowVersion> Versions { get; set; } = new List<FlowVersion>();
}
