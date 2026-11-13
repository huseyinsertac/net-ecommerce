using Shop.Application.Abstractions;
using Shop.SharedKernel;

namespace Shop.Api.ErrorHandling;

public static class ErrorResults
{
    /// <summary>Maps a domain <see cref="Error"/> to an RFC 9457 problem response (type = error code, title = message).</summary>
    public static IResult ToProblem(this Error error) => error switch
    {
        ValidationError validation => Results.ValidationProblem(
            validation.Errors.ToDictionary(e => e.Key, e => e.Value),
            title: validation.Message,
            type: validation.Code),
        _ => Results.Problem(title: error.Message, type: error.Code, statusCode: StatusCodeFor(error.Type)),
    };

    private static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status500InternalServerError,
    };
}
