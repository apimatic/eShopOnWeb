using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

internal static class CallerIdentity
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }

        return name;
    }
}
