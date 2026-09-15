using System;
using System.Linq;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Translates an Ardalis <see cref="Result"/> into a minimal-API <see cref="IResult"/>.</summary>
internal static class ApiResults
{
    public static IResult ToApiResult<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        switch (result.Status)
        {
            case ResultStatus.Ok:
            case ResultStatus.Created:
                return onSuccess(result.Value);
            case ResultStatus.NoContent:
                return Results.NoContent();
            case ResultStatus.NotFound:
                return Results.NotFound();
            case ResultStatus.Invalid:
                return Results.BadRequest(new { errors = result.ValidationErrors.Select(e => e.ErrorMessage) });
            case ResultStatus.Conflict:
                return Results.Conflict(new { errors = result.Errors });
            case ResultStatus.Forbidden:
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            case ResultStatus.Unauthorized:
                return Results.Unauthorized();
            case ResultStatus.Unavailable:
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            default:
                return Results.Problem(
                    detail: result.Errors.Any() ? string.Join("; ", result.Errors) : "An unexpected error occurred.",
                    statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public static IResult ToApiResult(this Result result)
    {
        switch (result.Status)
        {
            case ResultStatus.Ok:
            case ResultStatus.Created:
            case ResultStatus.NoContent:
                return Results.NoContent();
            case ResultStatus.NotFound:
                return Results.NotFound();
            case ResultStatus.Invalid:
                return Results.BadRequest(new { errors = result.ValidationErrors.Select(e => e.ErrorMessage) });
            case ResultStatus.Conflict:
                return Results.Conflict(new { errors = result.Errors });
            case ResultStatus.Forbidden:
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            case ResultStatus.Unauthorized:
                return Results.Unauthorized();
            default:
                return Results.Problem(
                    detail: result.Errors.Any() ? string.Join("; ", result.Errors) : "An unexpected error occurred.",
                    statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
