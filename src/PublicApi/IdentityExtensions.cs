using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class IdentityExtensions
{
    /// <summary>
    /// The caller's identity from the JWT. Tokens issued by this API carry the
    /// username as the name claim; depending on claim mapping it surfaces as
    /// ClaimTypes.Name or "unique_name".
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Name)?.Value
        ?? user.FindFirst("unique_name")?.Value
        ?? user.FindFirst("name")?.Value
        ?? user.Identity?.Name;
}
