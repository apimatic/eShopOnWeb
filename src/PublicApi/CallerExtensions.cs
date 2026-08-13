using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Helpers for reading the caller's identity from the JWT. The buyer id used across the app is the
/// user name (the token's <see cref="ClaimTypes.Name"/> claim), matching how orders are keyed.
/// </summary>
public static class CallerExtensions
{
    public static string? GetBuyerId(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}
