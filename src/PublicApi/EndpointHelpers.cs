using System.Linq;
using System.Security.Claims;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi;

internal static class EndpointHelpers
{
    public static string? GetBuyerId(HttpContext httpContext)
    {
        return httpContext.User.Identity?.Name;
    }

    public static bool IsAdministrator(HttpContext httpContext)
    {
        return httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
    }

    public static HttpResult ToHttpResult(this Result result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok();
        }

        return MapFailure(result.Status, result.ValidationErrors, result.Errors);
    }

    public static HttpResult ToHttpResult<T>(this Result<T> result, Func<T, HttpResult> onSuccess)
    {
        if (result.IsSuccess)
        {
            return onSuccess(result.Value);
        }

        return MapFailure(result.Status, result.ValidationErrors, result.Errors);
    }

    private static HttpResult MapFailure(
        ResultStatus status,
        IEnumerable<ValidationError> validationErrors,
        IEnumerable<string> errors)
    {
        var message = validationErrors.FirstOrDefault()?.ErrorMessage
            ?? errors.FirstOrDefault()
            ?? status.ToString();

        return status switch
        {
            ResultStatus.NotFound => Results.NotFound(new { message }),
            ResultStatus.Invalid => Results.BadRequest(new { message }),
            ResultStatus.Unauthorized => Results.Unauthorized(),
            ResultStatus.Forbidden => Results.Forbid(),
            _ => Results.Problem(message)
        };
    }
}
