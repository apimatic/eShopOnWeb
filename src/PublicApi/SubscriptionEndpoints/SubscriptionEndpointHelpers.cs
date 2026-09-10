using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointHelpers
{
    /// <summary>
    /// Builds the subscriber identity from the authenticated caller's token. Returns false when the token
    /// carries no name claim (which [Authorize] should prevent, but we guard defensively).
    /// </summary>
    public static bool TryGetSubscriber(ClaimsPrincipal principal, out SubscriberIdentity subscriber)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            subscriber = null!;
            return false;
        }

        subscriber = SubscriberIdentity.FromUsername(username);
        return true;
    }

    /// <summary>Translates a Maxio failure into an appropriate HTTP response for the API caller.</summary>
    public static IResult ToErrorResult(MaxioBillingException exception)
    {
        var status = exception.UpstreamStatusCode switch
        {
            404 => StatusCodes.Status404NotFound,
            400 or 422 => StatusCodes.Status400BadRequest,
            // Auth/rate-limit/unknown upstream issues are not the caller's fault — surface as a gateway error
            // without leaking upstream credentials or internals.
            _ => StatusCodes.Status502BadGateway,
        };

        var detail = status == StatusCodes.Status502BadGateway
            ? "The billing provider is currently unavailable. Please try again later."
            : exception.Message;

        return Results.Problem(detail: detail, statusCode: status, title: "Subscription request failed");
    }
}
