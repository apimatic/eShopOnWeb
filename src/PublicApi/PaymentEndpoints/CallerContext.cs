using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Reads the caller's identity from the validated JWT. The buyer id is the token's name claim; the
/// operator flag is the administrator role the project already uses for its privileged endpoints.
/// </summary>
public static class CallerContext
{
    public static string GetBuyerId(this ClaimsPrincipal user) =>
        user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(Constants.Roles.ADMINISTRATORS);
}
