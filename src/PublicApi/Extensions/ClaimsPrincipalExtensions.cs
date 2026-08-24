using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Gets the authenticated username from the JWT, tolerating both mapped
    /// (ClaimTypes.Name) and unmapped ("unique_name") claim representations.
    /// </summary>
    public static string? GetUsername(this ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Name)?.Value
        ?? user.FindFirst("unique_name")?.Value
        ?? user.Identity?.Name;
}
