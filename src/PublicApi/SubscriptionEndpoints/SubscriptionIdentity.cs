using System.Security.Claims;
using CoreSubscribeRequest = Microsoft.eShopWeb.ApplicationCore.Subscriptions.SubscribeRequest;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Derives the billing identity from the authenticated caller. The JWT carries the username as
/// <see cref="ClaimTypes.Name"/> (see IdentityTokenClaimService); that username is the stable Maxio
/// customer reference. No identity ever comes from the request body.
/// </summary>
internal static class SubscriptionIdentity
{
    /// <summary>The caller's stable reference (username), or null when unauthenticated.</summary>
    public static string? GetUserReference(ClaimsPrincipal user)
    {
        var name = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>Builds the (ApplicationCore) subscribe request for the given plan from the caller's identity.</summary>
    public static CoreSubscribeRequest ToSubscribeRequest(string userReference, string planHandle)
    {
        var email = LooksLikeEmail(userReference) ? userReference : $"{userReference}@users.eshoponweb.local";
        var localPart = userReference.Contains('@') ? userReference[..userReference.IndexOf('@')] : userReference;

        return new CoreSubscribeRequest
        {
            UserReference = userReference,
            Email = email,
            FirstName = string.IsNullOrWhiteSpace(localPart) ? userReference : localPart,
            LastName = "eShopOnWeb Shopper",
            PlanHandle = planHandle
        };
    }

    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at < value.Length - 1;
    }
}
