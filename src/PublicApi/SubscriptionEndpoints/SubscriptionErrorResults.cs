using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps a <see cref="SubscriptionBillingException"/> onto a coherent HTTP problem response. Note the
/// deliberate split: provider auth/rate-limit/transport failures (our fault) become 502, while
/// validation/not-found/conflict (the caller's to act on) keep their client-facing status. Only the
/// exception's caller-safe message is surfaced — never SDK/type detail.
/// </summary>
internal static class SubscriptionErrorResults
{
    public static IResult From(SubscriptionBillingException ex)
    {
        var statusCode = ex.Kind switch
        {
            SubscriptionBillingErrorKind.InvalidRequest => StatusCodes.Status400BadRequest,
            SubscriptionBillingErrorKind.NotFound => StatusCodes.Status404NotFound,
            SubscriptionBillingErrorKind.Conflict => StatusCodes.Status409Conflict,
            SubscriptionBillingErrorKind.ProviderUnavailable => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(detail: ex.Message, statusCode: statusCode, title: "Subscription billing error");
    }
}
