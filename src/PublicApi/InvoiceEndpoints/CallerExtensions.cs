using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Resolves the caller's identity and role from the validated JWT. The caller's identity always comes
/// from the token, never from anything in the request body.
/// </summary>
public static class CallerExtensions
{
    public static string GetCallerId(this ClaimsPrincipal user)
    {
        // The authenticate endpoint issues the username (email) as the name claim.
        var id = user.Identity?.Name
                 ?? user.FindFirstValue(ClaimTypes.Name)
                 ?? user.FindFirstValue("unique_name")
                 ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return id ?? string.Empty;
    }

    public static bool IsOperator(this ClaimsPrincipal user) =>
        user.IsInRole(Constants.Roles.ADMINISTRATORS);
}
