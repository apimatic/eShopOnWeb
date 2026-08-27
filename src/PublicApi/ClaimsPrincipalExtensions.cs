using System.Linq;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's identity, taken from the JWT. Tolerates the different claim types a
    /// JWT handler may surface for the token's name claim.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.Name)?.Value
           ?? user.FindFirst("unique_name")?.Value
           ?? user.FindFirst("name")?.Value
           ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? user.Identity?.Name;

    public static bool IsAdministrator(this ClaimsPrincipal user)
        => user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
           || user.FindAll("role").Select(c => c.Value).Contains(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
