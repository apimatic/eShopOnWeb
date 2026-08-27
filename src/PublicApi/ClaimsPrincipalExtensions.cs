using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's identity from the JWT. Tokens issued by this API carry the
    /// username as ClaimTypes.Name; fallbacks cover remapped claim types.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.FindFirst("unique_name")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.Identity?.Name;
    }
}
