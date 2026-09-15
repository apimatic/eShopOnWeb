using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Reads the caller's identity from the JWT. The buyer/owner id used throughout the shopping flows is
/// the authenticated user name carried in the token.
/// </summary>
public static class CurrentUserExtensions
{
    public static string? GetUserId(this ClaimsPrincipal principal) =>
        principal?.Identity?.Name;
}
