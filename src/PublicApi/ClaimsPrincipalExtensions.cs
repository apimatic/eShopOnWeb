using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Helpers for reading the caller's identity from the bearer token. The username is carried as
/// <see cref="ClaimTypes.Name"/> and is what identifies the shopper (the order buyer id and contact
/// number owner id).
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static string? GetUserId(this ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.Name);
}
