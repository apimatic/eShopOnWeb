using System.Linq;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ResultHttpExtensions
{
    public static Microsoft.AspNetCore.Http.IResult ToHttp(this Result result)
    {
        return result.Status switch
        {
            ResultStatus.Ok => Results.Ok(),
            ResultStatus.NotFound => Results.NotFound(),
            ResultStatus.Invalid => Results.BadRequest(new { errors = result.ValidationErrors.Select(e => e.ErrorMessage) }),
            ResultStatus.Error => Results.Json(new { errors = result.Errors }, statusCode: 502),
            _ => Results.Json(new { errors = result.Errors }, statusCode: 500)
        };
    }

    public static Microsoft.AspNetCore.Http.IResult ToHttp<T>(this Result<T> result, System.Func<T, Microsoft.AspNetCore.Http.IResult> onSuccess)
    {
        if (result.IsSuccess)
        {
            return onSuccess(result.Value);
        }

        return result.Status switch
        {
            ResultStatus.NotFound => Results.NotFound(),
            ResultStatus.Invalid => Results.BadRequest(new { errors = result.ValidationErrors.Select(e => e.ErrorMessage) }),
            ResultStatus.Error => Results.Json(new { errors = result.Errors }, statusCode: 502),
            _ => Results.Json(new { errors = result.Errors }, statusCode: 500)
        };
    }
}
