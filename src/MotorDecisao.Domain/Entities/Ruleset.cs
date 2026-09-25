using MotorDecisao.Domain.Common;

namespace MotorDecisao.Domain.Entities;

/// <summary>
/// A named collection of <see cref="Rule"/> records, evaluated together. A ruleset
/// doubles as a scorecard: rules with a scoring effect accumulate points, and the
/// <see cref="ApprovalThreshold"/> can be used by the flow to interpret the total.
/// </summary>
public class Ruleset : Entity
{
    /// <summary>Owning flow version.</summary>
    public Guid FlowVersionId { get; set; }
    public FlowVersion? FlowVersion { get; set; }

    /// <summary>Name shown in the editor (e.g. "Base scorecard").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of the ruleset's purpose.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Minimum accumulated score to be considered a pass by consumers of this
    /// ruleset. Interpretation is left to the flow; stored here for convenience.
    /// </summary>
    public decimal? ApprovalThreshold { get; set; }

    /// <summary>Rules evaluated as part of this ruleset, in <see cref="Rule.Order"/>.</summary>
    public ICollection<Rule> Rules { get; set; } = new List<Rule>();
}
