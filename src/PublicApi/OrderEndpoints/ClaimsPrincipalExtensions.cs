using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's buyer id (username/email), matching Order.BuyerId. Null if unauthenticated.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>Whether the caller holds the administrator (operator) role.</summary>
    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(Constants.Roles.ADMINISTRATORS);
}
