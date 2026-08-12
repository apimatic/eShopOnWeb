using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Helpers for reading the caller's identity out of the JWT. The buyer id is the name claim the
/// authenticate endpoint puts in the token, which is also the value stored as an order's BuyerId.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static string? GetBuyerId(this ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;

    public static bool IsAdministrator(this ClaimsPrincipal principal)
        => principal.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
