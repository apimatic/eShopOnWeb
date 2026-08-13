using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Resolves the caller's identity from the validated JWT. Shopper-scoped endpoints use this as the
/// owner id so a caller only ever acts on their own data.
/// </summary>
public static class CurrentUser
{
    public static string GetUserId(this ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("unique_name");

        if (string.IsNullOrWhiteSpace(name))
            throw new ResourceNotFoundException("The caller's identity could not be determined.");

        return name;
    }
}
