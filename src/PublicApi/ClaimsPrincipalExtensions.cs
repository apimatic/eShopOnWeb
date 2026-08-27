using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's identity from the JWT (the token carries ClaimTypes.Name = username).</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
}
