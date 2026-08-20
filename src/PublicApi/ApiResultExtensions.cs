using System;
using System.Linq;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ApiResultExtensions
{
    public static HttpResult ToHttpResult<T>(this Result<T> result, Func<T, HttpResult> onSuccess)
    {
        if (result.IsSuccess)
        {
            return onSuccess(result.Value);
        }

        return MapFailure(result.Status, result.ValidationErrors.Select(v => v.ErrorMessage).Concat(result.Errors).ToArray());
    }

    public static HttpResult ToHttpResult(this Result result, Func<HttpResult> onSuccess)
    {
        if (result.IsSuccess)
        {
            return onSuccess();
        }

        return MapFailure(result.Status, result.ValidationErrors.Select(v => v.ErrorMessage).Concat(result.Errors).ToArray());
    }

    private static HttpResult MapFailure(ResultStatus status, string[] errors)
    {
        return status switch
        {
            ResultStatus.NotFound => Results.NotFound(new { errors }),
            ResultStatus.Invalid => Results.BadRequest(new { errors }),
            ResultStatus.Unauthorized => Results.Unauthorized(),
            ResultStatus.Forbidden => Results.Json(new { errors }, statusCode: StatusCodes.Status403Forbidden),
            ResultStatus.Error => Results.Json(new { errors }, statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Json(new { errors }, statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}

internal static class HttpUserExtensions
{
    public static string GetRequiredBuyerId(this HttpContext httpContext)
    {
        var name = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }

        return name;
    }
}
