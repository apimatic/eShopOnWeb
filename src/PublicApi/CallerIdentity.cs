using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Resolves the calling shopper's identity from the JWT. The name claim is the buyer/owner key used
/// throughout ordering and notifications, so a caller only ever acts on their own data.
/// </summary>
public static class CallerIdentity
{
    public static string GetOwnerId(ClaimsPrincipal user)
    {
        // The token issued by the authenticate endpoint carries the user name as the Name claim.
        var name = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
    }
}
