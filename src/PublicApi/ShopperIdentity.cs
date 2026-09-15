using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ShopperIdentity
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

    public static bool IsAdministrator(this ClaimsPrincipal user)
        => user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
