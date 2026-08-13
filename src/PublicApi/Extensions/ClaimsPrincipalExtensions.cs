using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The signed-in user's identity, used as the buyer id that scopes a shopper to their own data.
    /// </summary>
    public static string? GetUserName(this ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("unique_name")
            ?? principal.Identity?.Name;
    }
}
