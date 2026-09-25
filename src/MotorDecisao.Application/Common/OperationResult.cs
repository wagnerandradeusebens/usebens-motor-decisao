namespace MotorDecisao.Application.Common;

/// <summary>
/// The category of an operation failure, so the API layer can map to the right
/// HTTP status without the Application layer knowing about HTTP.
/// </summary>
public enum OperationError
{
    None,
    NotFound,
    Conflict,
    Validation
}

/// <summary>
/// A lightweight result carrying either a value or a categorized failure with a
/// human-readable (pt-BR) message.
/// </summary>
public readonly struct OperationResult<T>
{
    public bool Success { get; }
    public T? Value { get; }
    public OperationError Error { get; }
    public string? Message { get; }

    private OperationResult(bool success, T? value, OperationError error, string? message)
    {
        Success = success;
        Value = value;
        Error = error;
        Message = message;
    }

    public static OperationResult<T> Ok(T value) => new(true, value, OperationError.None, null);
    public static OperationResult<T> NotFound(string message) => new(false, default, OperationError.NotFound, message);
    public static OperationResult<T> Conflict(string message) => new(false, default, OperationError.Conflict, message);
    public static OperationResult<T> Invalid(string message) => new(false, default, OperationError.Validation, message);
}
