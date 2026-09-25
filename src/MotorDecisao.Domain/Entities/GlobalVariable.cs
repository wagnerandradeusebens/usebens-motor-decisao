using MotorDecisao.Domain.Common;

namespace MotorDecisao.Domain.Entities;

/// <summary>
/// A named, reusable Excel-like formula shared across every policy (Crivo
/// "Variáveis Globais"). Unlike <see cref="Formula"/> (scoped to a single flow
/// version), a global variable has no owning version: it is available to all
/// flows and referenced the same way, by <c>{Key}</c>.
///
/// When a flow defines a local variable with the same <see cref="Key"/> as a
/// global one, the local definition wins for that flow.
/// </summary>
public class GlobalVariable : Entity
{
    /// <summary>
    /// Identifier used to reference the variable's result in formulas
    /// (<c>{Key}</c>). Must be a valid variable name and is unique globally.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Friendly label shown in the editor.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The Excel-like expression, e.g. <c>ROUND('renda' * 0.3; 2)</c>.</summary>
    public string Expression { get; set; } = string.Empty;
}
