using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The JWT issued by this API carries the username as ClaimTypes.Name.
    /// </summary>
    public static string? GetUserName(this ClaimsPrincipal user) =>
        user.Identity?.Name
        ?? user.FindFirst(ClaimTypes.Name)?.Value
        ?? user.FindFirst("unique_name")?.Value;

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
