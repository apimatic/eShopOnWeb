using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Reads the caller's identity from the validated JWT (the token carries the shopper's name and roles).</summary>
internal static class CallerIdentity
{
    public const string AdministratorsRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public static string BuyerId(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name
                   ?? user.FindFirstValue(ClaimTypes.Name)
                   ?? user.FindFirstValue("name");
        if (string.IsNullOrEmpty(name))
        {
            // Should never happen behind [Authorize], but fail safe rather than act on a null buyer.
            throw new PaymentAccessDeniedException("The bearer token does not identify a shopper.");
        }
        return name;
    }

    public static bool IsAdministrator(ClaimsPrincipal user) => user.IsInRole(AdministratorsRole);
}
