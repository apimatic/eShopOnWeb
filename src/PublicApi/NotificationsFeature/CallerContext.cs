using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.NotificationsFeature;

/// <summary>
/// Reads the signed-in caller's identity from the JWT for the SMS-notification endpoints. The
/// caller's buyer id is their user name (an email), matching how orders record their buyer.
/// </summary>
public static class CallerContext
{
    /// <summary>The caller's buyer id (user name), or null if unauthenticated.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        return string.IsNullOrEmpty(name) ? user.FindFirstValue(ClaimTypes.Name) : name;
    }

    /// <summary>True when the caller holds the administrator (operator) role.</summary>
    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(Constants.Roles.ADMINISTRATORS);
}
