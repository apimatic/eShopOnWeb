using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class EndpointUser
{
    public static string? GetUserName(ClaimsPrincipal principal)
    {
        var name = principal.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        name = principal.FindFirstValue("unique_name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        name = principal.FindFirstValue("name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return principal.Identity?.Name;
    }
}
