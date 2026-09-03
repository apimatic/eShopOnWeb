using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionErrorResults
{
    /// <summary>
    /// Maps a <see cref="MaxioBillingException"/> to a caller-facing result. The message is already
    /// caller-safe (constructed at the billing boundary), and the status distinguishes "your request was
    /// invalid" (4xx) from "the provider is unavailable" (5xx) — a provider failure that is our fault is
    /// never reported to the caller as a client error.
    /// </summary>
    public static IResult ToResult(this MaxioBillingException ex)
    {
        var status = ex.Kind switch
        {
            MaxioBillingFailureKind.InvalidRequest => HttpStatusCode.UnprocessableEntity,
            MaxioBillingFailureKind.NotFound => HttpStatusCode.NotFound,
            MaxioBillingFailureKind.Conflict => HttpStatusCode.Conflict,
            MaxioBillingFailureKind.ProviderUnavailable => HttpStatusCode.ServiceUnavailable,
            _ => HttpStatusCode.InternalServerError
        };

        return Results.Json(new { message = ex.Message, statusCode = (int)status }, statusCode: (int)status);
    }
}
