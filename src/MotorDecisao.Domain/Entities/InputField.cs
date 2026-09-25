using MotorDecisao.Domain.Common;
using MotorDecisao.Domain.Enums;

namespace MotorDecisao.Domain.Entities;

/// <summary>
/// A declared input field of a policy version: what the calling system is
/// expected to send in the proposal. Declaring fields drives the execution
/// portal's form, the formula editor autocomplete, and documents the contract of
/// the policy. It is descriptive — the engine still tolerates missing/extra
/// fields at runtime (a missing field evaluates as blank).
/// </summary>
public class InputField : Entity
{
    /// <summary>Owning flow version.</summary>
    public Guid FlowVersionId { get; set; }
    public FlowVersion? FlowVersion { get; set; }

    /// <summary>Technical name used in formulas (e.g. <c>renda_mensal</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-friendly label shown in the portal (e.g. "Renda mensal").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Expected value type, used to render the right input control.</summary>
    public InputFieldType Type { get; set; } = InputFieldType.Text;

    /// <summary>Whether the portal should require a value before executing.</summary>
    public bool Required { get; set; }

    /// <summary>Display order in the portal form.</summary>
    public int Order { get; set; }
}
