using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

internal static class BuyerIdentity
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new UnauthorizedAccessException("The caller's identity was not present on the token.");
        }

        return name;
    }
}
