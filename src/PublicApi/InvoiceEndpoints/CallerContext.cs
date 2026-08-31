using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Reads the caller's identity from the JWT. The buyer id is the authenticated username (which in
/// eShopOnWeb equals the user's email), and operator status is the administrator role the rest of the
/// API already uses for its privileged endpoints.
/// </summary>
internal static class CallerContext
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new InvoiceAccessDeniedException("The caller could not be identified from the token.");
        }
        return name;
    }

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
