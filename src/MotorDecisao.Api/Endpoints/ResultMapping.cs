using MotorDecisao.Application.Common;

namespace MotorDecisao.Api.Endpoints;

/// <summary>Maps <see cref="OperationResult{T}"/> failures to HTTP results.</summary>
public static class ResultMapping
{
    public static IResult ToHttp<T>(this OperationResult<T> result, Func<T, IResult>? onSuccess = null)
    {
        if (result.Success)
        {
            return onSuccess is not null
                ? onSuccess(result.Value!)
                : Results.Ok(result.Value);
        }

        var payload = new { error = result.Message };
        return result.Error switch
        {
            OperationError.NotFound => Results.NotFound(payload),
            OperationError.Conflict => Results.Conflict(payload),
            OperationError.Validation => Results.BadRequest(payload),
            _ => Results.BadRequest(payload)
        };
    }
}
