using System;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>Maps an <see cref="OperationResult{T}"/> outcome onto an HTTP result.</summary>
public static class ResultMapping
{
    public static IResult ToHttpResult<T>(this OperationResult<T> result) =>
        result.ToHttpResult(value => Results.Ok(value));

    public static IResult ToHttpResult<T>(this OperationResult<T> result, Func<T, IResult> onSuccess) =>
        result.Outcome switch
        {
            InvoiceOutcome.Success => onSuccess(result.Value!),
            InvoiceOutcome.NotFound => Results.NotFound(new { message = result.Error }),
            InvoiceOutcome.Conflict => Results.Conflict(new { message = result.Error }),
            InvoiceOutcome.BadRequest => Results.BadRequest(new { message = result.Error }),
            // The provider itself failed (transport/integration), not the caller's request.
            InvoiceOutcome.ProviderError => Results.Problem(detail: result.Error, statusCode: StatusCodes.Status502BadGateway),
            _ => Results.Problem("Unexpected outcome.")
        };
}
