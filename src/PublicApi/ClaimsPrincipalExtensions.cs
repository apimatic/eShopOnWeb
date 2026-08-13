using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's identity (the JWT name claim), which eShop uses as the buyer id for orders,
    /// contact numbers and notifications.
    /// </summary>
    public static string? GetUserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;
}
