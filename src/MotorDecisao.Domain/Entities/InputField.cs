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

    /// <summary>Descrição do campo (o que é, formato esperado) — documenta a request.</summary>
    public string? Description { get; set; }

    /// <summary>Valor de exemplo do campo, usado no payload de exemplo da request.</summary>
    public string? Example { get; set; }

    /// <summary>
    /// Grupo/assunto do campo na request (ex.: <c>proponente</c>, <c>operacao</c>).
    /// Quando definido, o campo é referenciado nas fórmulas como
    /// <c>'grupo.campo'</c> e viaja aninhado no payload. Vazio = campo na raiz.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>Display order in the portal form.</summary>
    public int Order { get; set; }
}
