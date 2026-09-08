using System;
using System.Linq;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps Ardalis.Result outcomes from the subscription service onto HTTP responses.
/// </summary>
internal static class SubscriptionResultMapper
{
    public static IResult ToHttpResult<T>(Result<T> result, Func<T, IResult> onSuccess)
    {
        return result.Status switch
        {
            ResultStatus.Ok => onSuccess(result.Value),
            ResultStatus.NotFound => Results.NotFound(new { message = string.Join("; ", result.Errors) }),
            ResultStatus.Invalid => Results.BadRequest(new
            {
                message = "Invalid request.",
                errors = result.ValidationErrors.Select(e => e.ErrorMessage).ToList()
            }),
            ResultStatus.Error => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Billing system error",
                detail: string.Join("; ", result.Errors)),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Unexpected error")
        };
    }
}
