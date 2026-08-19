using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>
/// Resolves the caller's identity from the JWT. The token issued by this API carries the user
/// name as <see cref="ClaimTypes.Name"/>; that value is used as the owner id throughout so a
/// shopper only ever acts on their own contact numbers and orders.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static string GetOwnerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name ?? string.Empty;
}
