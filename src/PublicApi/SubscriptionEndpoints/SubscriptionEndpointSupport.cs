using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointSupport
{
    /// <summary>The JWT carries only the user name (ClaimTypes.Name); identity is never taken from the body or the URL.</summary>
    public static string? UserName(ClaimsPrincipal user) =>
        user.Identity?.Name
        ?? user.FindFirst(ClaimTypes.Name)?.Value
        ?? user.FindFirst("unique_name")?.Value
        ?? user.FindFirst("name")?.Value;
}
