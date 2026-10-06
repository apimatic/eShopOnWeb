using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps the billing integration's failure kinds onto deliberate HTTP outcomes, with
/// caller-safe messages only. Client-fixable failures surface as 4xx; Maxio-side
/// failures as 502; unreachability and missing configuration as 503.
/// </summary>
internal static class BillingErrorResults
{
    public static IResult From(MaxioBillingException exception, Guid correlationId)
    {
        var (statusCode, message, errors) = exception.Kind switch
        {
            MaxioBillingFailureKind.NotConfigured =>
                (503, exception.Message, exception.Details),
            MaxioBillingFailureKind.NotFound =>
                (404, exception.Message, exception.Details),
            MaxioBillingFailureKind.Validation =>
                (422, exception.Message, exception.Details),
            MaxioBillingFailureKind.Provider =>
                (502, exception.Message, exception.Details),
            MaxioBillingFailureKind.Unavailable =>
                (503, exception.Message, exception.Details),
            _ =>
                (500, "An unexpected billing error occurred.", exception.Details)
        };

        return Results.Json(
            new BillingErrorResponse(correlationId)
            {
                Message = message,
                Errors = errors
            },
            statusCode: statusCode);
    }
}