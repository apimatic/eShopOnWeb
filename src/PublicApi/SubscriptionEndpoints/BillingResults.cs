using System.Net;
using System.Security.Claims;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.MaxioBilling.Exceptions;
using Microsoft.eShopWeb.MaxioBilling.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The one place billing failures become HTTP responses, so the same kind of failure looks the
/// same on every subscription endpoint.
/// </summary>
internal static class BillingResults
{
    /// <summary>
    /// Builds the subscriber identity from the bearer token. The eShopOnWeb token carries the
    /// user's login name and nothing else, so that is the identity the billing customer is keyed on.
    /// </summary>
    public static SubscriberIdentity? ToSubscriber(this ClaimsPrincipal? principal)
    {
        var userName = principal?.Identity?.Name ?? principal?.FindFirstValue(ClaimTypes.Name);

        return string.IsNullOrWhiteSpace(userName) ? null : SubscriberIdentity.ForUser(userName);
    }

    /// <summary>The response for a token that carries no usable user name.</summary>
    public static IResult MissingIdentity() =>
        Error(HttpStatusCode.Unauthorized, "The access token does not identify a user.");

    /// <summary>
    /// Translates a billing failure into a status the caller can act on. A provider rejection stays
    /// a client error; anything the caller cannot fix becomes a server error.
    /// </summary>
    public static IResult Problem(BillingException exception)
    {
        var status = exception.Kind switch
        {
            BillingFailureKind.NotConfigured => HttpStatusCode.ServiceUnavailable,
            BillingFailureKind.Configuration => HttpStatusCode.InternalServerError,
            BillingFailureKind.PlanNotFound => HttpStatusCode.NotFound,
            BillingFailureKind.Rejected => HttpStatusCode.BadRequest,
            BillingFailureKind.ProviderUnavailable => HttpStatusCode.ServiceUnavailable,
            BillingFailureKind.ProviderError => HttpStatusCode.BadGateway,
            BillingFailureKind.OutcomeUnknown => HttpStatusCode.BadGateway,
            _ => HttpStatusCode.BadGateway
        };

        // exception.Message is already caller-safe: the integration boundary never puts SDK or
        // provider text on the wire.
        return Error(status, exception.Message);
    }

    private static IResult Error(HttpStatusCode status, string message) =>
        Results.Json(
            new ErrorDetails { StatusCode = (int)status, Message = message },
            statusCode: (int)status);
}
