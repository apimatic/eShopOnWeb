using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Helpers for reading the caller's identity from the bearer token. The buyer id used throughout the
/// app is the authenticated user name (the same value existing orders are keyed on).
/// </summary>
public static class ApiUser
{
    public static string? GetBuyerId(this ClaimsPrincipal user) => user?.Identity?.Name;
}
