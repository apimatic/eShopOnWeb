using System.Linq;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Maps an unsuccessful <see cref="Ardalis.Result"/> to a minimal-API <see cref="IResult"/>.</summary>
public static class ResultExtensions
{
    public static IResult ToFailureResult(this Result result)
        => Map(result.Status, result.Errors?.ToArray() ?? System.Array.Empty<string>(),
               result.ValidationErrors);

    public static IResult ToFailureResult<T>(this Result<T> result)
        => Map(result.Status, result.Errors?.ToArray() ?? System.Array.Empty<string>(),
               result.ValidationErrors);

    private static IResult Map(ResultStatus status, string[] errors,
        System.Collections.Generic.IEnumerable<ValidationError>? validationErrors)
    {
        switch (status)
        {
            case ResultStatus.NotFound:
                return Results.NotFound();
            case ResultStatus.Forbidden:
                // Use an explicit 403 rather than Results.Forbid(): the default forbid scheme is the
                // Identity cookie (from AddIdentity), which would 302-redirect to an access-denied page.
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            case ResultStatus.Unauthorized:
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            case ResultStatus.Invalid:
                var problems = (validationErrors ?? System.Linq.Enumerable.Empty<ValidationError>())
                    .GroupBy(e => string.IsNullOrEmpty(e.Identifier) ? "error" : e.Identifier)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                return Results.ValidationProblem(problems);
            default:
                var detail = errors.Length > 0 ? string.Join("; ", errors) : "The request could not be completed.";
                return Results.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
