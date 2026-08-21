using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Reads the authenticated caller's identity from the JWT. The token carries the username as the name claim.</summary>
public static class CurrentUserExtensions
{
    public static string GetUserName(this ClaimsPrincipal user) =>
        user.Identity?.Name
        ?? user.FindFirstValue(ClaimTypes.Name)
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? string.Empty;
}
