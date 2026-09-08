using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ApiUserContext
{
    public static string? GetUserName(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(user.Identity.Name))
        {
            return user.Identity.Name;
        }

        return user.FindFirst(ClaimTypes.Name)?.Value;
    }
}
