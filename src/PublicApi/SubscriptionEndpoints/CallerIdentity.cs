using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Reads the shopper identity from the bearer token. The subscription endpoints never take
/// a user name from the request body, so a caller can only act on their own subscriptions.
/// </summary>
internal static class CallerIdentity
{
    public static string? GetUserName(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        // Tokens issued by /api/authenticate carry the user name as ClaimTypes.Name; the
        // remaining lookups cover a token whose inbound claim mapping is turned off.
        var userName = principal.Identity?.Name
                       ?? FirstClaim(principal, ClaimTypes.Name)
                       ?? FirstClaim(principal, JwtRegisteredClaimNames.UniqueName)
                       ?? FirstClaim(principal, JwtRegisteredClaimNames.Sub);

        return string.IsNullOrWhiteSpace(userName) ? null : userName;
    }

    private static string? FirstClaim(ClaimsPrincipal principal, string type) =>
        principal.Claims.FirstOrDefault(c => c.Type == type)?.Value;
}
