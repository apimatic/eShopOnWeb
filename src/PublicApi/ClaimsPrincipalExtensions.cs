using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's identity, taken from the JWT.</summary>
    public static string GetUserName(this ClaimsPrincipal user)
    {
        return user.Identity?.Name ?? user.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
    }

    public static bool IsAdministrator(this ClaimsPrincipal user)
    {
        return user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
    }
}
