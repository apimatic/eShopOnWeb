using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionClaims
{
    public static string? GetUserName(ClaimsPrincipal principal)
    {
        return principal.Identity?.Name
               ?? principal.FindFirstValue(ClaimTypes.Name)
               ?? principal.FindFirstValue("unique_name");
    }
}
