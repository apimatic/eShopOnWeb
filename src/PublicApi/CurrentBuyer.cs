using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class CurrentBuyer
{
    public static string Id(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CheckoutException(401, "Authentication is required.", "UNAUTHENTICATED");
        }

        return name;
    }
}
