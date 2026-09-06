using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Derives the shopper a subscription request acts for from the bearer token — never from the
/// request body, so a caller cannot subscribe or read on behalf of somebody else.
/// </summary>
internal static class BillingIdentityResolver
{
    /// <summary>The name claim as written by <c>IdentityTokenClaimService</c> when it mints a JWT.</summary>
    private const string JwtUniqueNameClaim = "unique_name";

    /// <summary>
    /// The caller's billing identity, or <see langword="null"/> when the token carries no usable
    /// name — which should not happen behind <c>[Authorize]</c>, but is worth answering cleanly.
    /// </summary>
    public static BillingIdentity? FromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        var userName = FirstNonEmpty(
            principal.Identity?.Name,
            principal.FindFirstValue(ClaimTypes.Name),
            principal.FindFirstValue(JwtUniqueNameClaim),
            principal.FindFirstValue(ClaimTypes.Email));

        if (userName is null)
        {
            return null;
        }

        // eShopOnWeb user names are e-mail addresses, but only trust an actual e-mail claim to be
        // one; BillingIdentity falls back to the user name when there is none.
        var email = FirstNonEmpty(principal.FindFirstValue(ClaimTypes.Email));

        return new BillingIdentity(userName, email);
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate!.Trim();
            }
        }

        return null;
    }
}
