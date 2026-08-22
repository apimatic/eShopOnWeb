using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ResultHttp
{
    public static HttpResult ToHttp<T>(Result<T> result, Func<T, HttpResult> onSuccess)
    {
        if (result.IsSuccess)
        {
            return onSuccess(result.Value);
        }

        return Status(result.Status, result.Errors, result.ValidationErrors);
    }

    public static HttpResult ToHttp(Result result, Func<HttpResult> onSuccess)
    {
        if (result.IsSuccess)
        {
            return onSuccess();
        }

        return Status(result.Status, result.Errors, result.ValidationErrors);
    }

    private static HttpResult Status(ResultStatus status, IEnumerable<string> errors, IEnumerable<ValidationError> validationErrors)
    {
        var message = validationErrors?.FirstOrDefault()?.ErrorMessage
            ?? errors?.FirstOrDefault()
            ?? "The request could not be completed.";

        return status switch
        {
            ResultStatus.NotFound => Results.NotFound(new { message }),
            ResultStatus.Invalid => Results.BadRequest(new { message }),
            ResultStatus.Forbidden => Results.Json(new { message }, statusCode: StatusCodes.Status403Forbidden),
            ResultStatus.Unauthorized => Results.Unauthorized(),
            ResultStatus.Error => Results.Json(new { message }, statusCode: StatusCodes.Status502BadGateway),
            _ => Results.Json(new { message = "Unexpected error." }, statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
