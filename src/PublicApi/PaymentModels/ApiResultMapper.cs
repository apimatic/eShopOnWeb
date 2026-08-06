using System.Linq;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Maps an unsuccessful <see cref="Ardalis.Result"/> onto an HTTP failure result. Returns
/// <c>null</c> when the result is successful, so the endpoint builds its own success response.
/// </summary>
public static class ApiResultMapper
{
    public static Microsoft.AspNetCore.Http.IResult? MapFailure(Ardalis.Result.IResult result)
    {
        switch (result.Status)
        {
            case ResultStatus.Ok:
                return null;
            case ResultStatus.NotFound:
                return Results.NotFound(new { errors = result.Errors.ToArray() });
            case ResultStatus.Invalid:
                return Results.BadRequest(new { errors = result.ValidationErrors.Select(e => e.ErrorMessage).ToArray() });
            case ResultStatus.Forbidden:
                return Results.Forbid();
            case ResultStatus.Unauthorized:
                return Results.Unauthorized();
            default:
                return Results.BadRequest(new { errors = result.Errors.ToArray() });
        }
    }
}
