using MotorDecisao.Domain.Common;

namespace MotorDecisao.Domain.Entities;

/// <summary>
/// A named, reusable Excel-like formula scoped to a flow version. Computation
/// nodes and rules can reference these by <see cref="Key"/> to avoid repeating
/// expressions and to let business users maintain shared derived values
/// (e.g. <c>debt_to_income = monthly_debt / monthly_income</c>).
/// </summary>
public class Formula : Entity
{
    /// <summary>Owning flow version.</summary>
    public Guid FlowVersionId { get; set; }
    public FlowVersion? FlowVersion { get; set; }

    /// <summary>
    /// Identifier used to reference the formula's result elsewhere. Must be a
    /// valid variable name (letters, digits, underscore) and unique per version.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Friendly label shown in the editor.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The Excel-like expression, e.g. <c>ROUND(income * 0.3; 2)</c>.</summary>
    public string Expression { get; set; } = string.Empty;
}
