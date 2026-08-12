using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's username, which is the buyer identity used throughout the order/basket model.
    /// Read from the JWT name claim (with a couple of fallbacks for claim-mapping differences).
    /// </summary>
    public static string? GetUsername(this ClaimsPrincipal principal)
    {
        return principal.Identity?.Name
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("unique_name")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
