using MotorDecisao.Application.Common;

namespace MotorDecisao.Application.Flows;

/// <summary>
/// A global (cross-policy) reusable variable, as the editor and API see it.
/// </summary>
public sealed record GlobalVariableDto(
    Guid Id,
    string Key,
    string Label,
    string Expression,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>Body to create or update a global variable.</summary>
public sealed record GlobalVariableInput(string Key, string Label, string Expression);

/// <summary>
/// CRUD use cases for global variables. These are shared across every policy and
/// referenced in formulas by <c>{Key}</c>. A flow's own local variable with the
/// same key takes precedence for that flow.
/// </summary>
public interface IGlobalVariableService
{
    /// <summary>Lists all global variables, ordered by key.</summary>
    Task<IReadOnlyList<GlobalVariableDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Creates a global variable. The key must be unique and the expression must compile.</summary>
    Task<OperationResult<GlobalVariableDto>> CreateAsync(GlobalVariableInput input, CancellationToken ct = default);

    /// <summary>Updates an existing global variable.</summary>
    Task<OperationResult<GlobalVariableDto>> UpdateAsync(Guid id, GlobalVariableInput input, CancellationToken ct = default);

    /// <summary>Deletes a global variable.</summary>
    Task<OperationResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default);
}
