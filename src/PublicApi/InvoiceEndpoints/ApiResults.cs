using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Translates an application-core <see cref="Result{T}"/> into an HTTP response.
///
/// Every non-successful result the invoicing service returns is a deliberate business
/// outcome (the target does not exist for this caller, the input is invalid, or the
/// bill's state disallows the change); genuine provider or system faults surface as
/// exceptions and are handled by the global exception middleware. Because the version
/// of Ardalis.Result in use has no dedicated Conflict status, a state conflict is
/// carried as <see cref="ResultStatus.Error"/> and mapped here to HTTP 409.
/// </summary>
public static class ApiResults
{
    public static IResult From<T>(Result<T> result, Func<T, IResult> onSuccess)
    {
        return result.Status switch
        {
            ResultStatus.Ok => onSuccess(result.Value),
            ResultStatus.NotFound => Results.NotFound(Payload(result)),
            ResultStatus.Invalid => Results.BadRequest(ValidationPayload(result)),
            ResultStatus.Forbidden => Results.Json(Payload(result), statusCode: StatusCodes.Status403Forbidden),
            ResultStatus.Unauthorized => Results.Json(Payload(result), statusCode: StatusCodes.Status401Unauthorized),
            // A legitimately refused transition for the bill's current state.
            _ => Results.Json(Payload(result), statusCode: StatusCodes.Status409Conflict)
        };
    }

    private static object Payload<T>(Result<T> result) =>
        new { errors = result.Errors?.ToArray() ?? Array.Empty<string>() };

    private static object ValidationPayload<T>(Result<T> result)
    {
        var messages = new List<string>();
        if (result.Errors is not null) messages.AddRange(result.Errors);
        if (result.ValidationErrors is not null) messages.AddRange(result.ValidationErrors.Select(v => v.ErrorMessage));
        return new { errors = messages.ToArray() };
    }
}
