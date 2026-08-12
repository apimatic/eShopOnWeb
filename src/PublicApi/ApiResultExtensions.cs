using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using AspNetResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Bridges the application layer's <see cref="Ardalis.Result.Result"/> outcomes to Minimal API
/// results, and reads the caller's identity from the bearer token.
/// </summary>
public static class ApiResultExtensions
{
    /// <summary>The shopper's identity (their username / email) as carried by the token.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    /// <summary>Maps a non-success result to the matching HTTP problem response.</summary>
    public static AspNetResult ToProblemResult(this Ardalis.Result.IResult result)
    {
        return result.Status switch
        {
            ResultStatus.NotFound => Results.NotFound(ProblemBody(result, "The requested resource was not found.")),
            ResultStatus.Invalid => Results.ValidationProblem(ValidationErrors(result)),
            ResultStatus.Conflict => Results.Conflict(ProblemBody(result, "The request conflicts with the current state.")),
            ResultStatus.Forbidden => Results.Forbid(),
            ResultStatus.Unauthorized => Results.Unauthorized(),
            _ => Results.Problem(detail: FirstError(result, "An unexpected error occurred."), statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static object ProblemBody(Ardalis.Result.IResult result, string fallback) =>
        new { message = FirstError(result, fallback) };

    private static string FirstError(Ardalis.Result.IResult result, string fallback) =>
        result.Errors?.FirstOrDefault() ?? fallback;

    private static IDictionary<string, string[]> ValidationErrors(Ardalis.Result.IResult result)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var validationError in result.ValidationErrors)
        {
            var key = string.IsNullOrEmpty(validationError.Identifier) ? "request" : validationError.Identifier;
            errors[key] = errors.TryGetValue(key, out var existing)
                ? existing.Append(validationError.ErrorMessage).ToArray()
                : new[] { validationError.ErrorMessage };
        }

        if (errors.Count == 0)
        {
            errors["request"] = new[] { "The request was invalid." };
        }

        return errors;
    }
}
</content>
