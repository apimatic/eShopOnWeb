using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The one place a billing failure becomes an HTTP status - every subscription endpoint uses it.</summary>
public static class BillingResults
{
    public static IResult From(BillingException ex, Guid correlationId)
    {
        var status = ex.Kind switch
        {
            BillingErrorKind.InvalidRequest => StatusCodes.Status400BadRequest,
            BillingErrorKind.Conflict => StatusCodes.Status409Conflict,
            BillingErrorKind.Rejected => StatusCodes.Status422UnprocessableEntity,
            BillingErrorKind.ProviderMisconfigured => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status503ServiceUnavailable
        };

        return Results.Json(new BillingErrorResponse(correlationId)
        {
            StatusCode = status,
            Message = ex.Message,
            Errors = ex.Errors
        }, statusCode: status);
    }
}
