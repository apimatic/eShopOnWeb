using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the billing subscriber from the bearer token. The caller's identity comes from the token
/// and from nowhere else — no endpoint accepts a user identifier in its request body or query string.
/// </summary>
internal static class SubscriberIdentityResolver
{
    /// <summary>
    /// Claims that can carry the shopper's e-mail, in preference order. The tokens this API issues put
    /// the username (which is the e-mail) in <see cref="ClaimTypes.Name"/>; the rest are there so a token
    /// from a differently configured issuer still resolves rather than silently 401-ing.
    /// </summary>
    private static readonly string[] EmailClaimTypes =
    {
        ClaimTypes.Name,
        ClaimTypes.Email,
        JwtRegisteredClaimNames.Email,
        JwtRegisteredClaimNames.UniqueName,
        JwtRegisteredClaimNames.Sub
    };

    public static bool TryResolve(ClaimsPrincipal? principal, out SubscriberIdentity subscriber)
    {
        subscriber = default;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var email = principal.Identity.Name;
        if (string.IsNullOrWhiteSpace(email))
        {
            email = EmailClaimTypes
                .Select(principal.FindFirstValue)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        subscriber = new SubscriberIdentity(email.Trim());
        return true;
    }
}
