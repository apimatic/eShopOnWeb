using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The single place where billing failures become HTTP responses, so the same kind of failure always
/// produces the same status whichever endpoint hit it.
/// </summary>
internal static class BillingResults
{
    private const string Title = "Subscription billing";

    /// <summary>
    /// Maps a billing failure onto a status the caller can act on. A provider rejection stays a client
    /// error; only a genuinely unknown or unavailable provider becomes a 5xx, so a retrying caller is
    /// never told to retry something that can never succeed.
    /// </summary>
    public static IResult Problem(BillingProviderException exception)
    {
        var statusCode = exception.Kind switch
        {
            BillingFailureKind.Rejected => StatusCodes.Status400BadRequest,
            BillingFailureKind.NotFound => StatusCodes.Status404NotFound,
            BillingFailureKind.Conflict => StatusCodes.Status409Conflict,
            BillingFailureKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
            BillingFailureKind.Misconfigured => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status502BadGateway
        };

        // exception.Message is authored to be caller-safe; provider and framework text is only logged.
        return Results.Problem(detail: exception.Message, statusCode: statusCode, title: Title);
    }

    /// <summary>
    /// Reads the caller's identity from the bearer token. eShopOnWeb issues the user's email as the name
    /// claim, and that is the identity the provider-side customer is keyed off.
    /// </summary>
    public static string? GetSubscriberEmail(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name;

    /// <summary>Response for an authenticated principal that carries no usable identity.</summary>
    public static IResult MissingIdentity() => Results.Problem(
        detail: "The bearer token does not identify a user.",
        statusCode: StatusCodes.Status401Unauthorized,
        title: Title);
}
