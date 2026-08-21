using System;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Maps an Ardalis <see cref="Result"/>/<see cref="Result{T}"/> onto an HTTP result, consistently
/// across every payment endpoint. <see cref="ResultStatus.Error"/> is used by the services for
/// business-state conflicts (e.g. fulfilling an unpaid order) and maps to 409.
/// </summary>
public static class ApiResults
{
    public static IResult From<T>(Result<T> result, Func<T, object> map) => result.Status switch
    {
        ResultStatus.Ok => Results.Ok(map(result.Value)),
        ResultStatus.NotFound => Results.NotFound(),
        ResultStatus.Invalid => Results.BadRequest(new { errors = result.ValidationErrors }),
        ResultStatus.Error => Results.Json(new { errors = result.Errors }, statusCode: StatusCodes.Status409Conflict),
        ResultStatus.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
        ResultStatus.Unauthorized => Results.StatusCode(StatusCodes.Status401Unauthorized),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };

    public static IResult From(Result result) => result.Status switch
    {
        ResultStatus.Ok => Results.NoContent(),
        ResultStatus.NotFound => Results.NotFound(),
        ResultStatus.Invalid => Results.BadRequest(new { errors = result.ValidationErrors }),
        ResultStatus.Error => Results.Json(new { errors = result.Errors }, statusCode: StatusCodes.Status409Conflict),
        ResultStatus.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
        ResultStatus.Unauthorized => Results.StatusCode(StatusCodes.Status401Unauthorized),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };
}
