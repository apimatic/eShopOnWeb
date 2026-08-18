using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Reads the caller's identity from the validated JWT. The buyer id is the token's Name claim; a
/// caller is an operator when the token carries the administrator role.
/// </summary>
public static class CallerIdentityExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
