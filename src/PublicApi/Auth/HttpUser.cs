using System.Security.Claims;
using BlazorShared.Authorization;
using System;

namespace Microsoft.eShopWeb.PublicApi.Auth;

internal static class HttpUser
{
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("The caller is not authenticated.");
        }

        return name;
    }

    public static bool IsAdministrator(ClaimsPrincipal user)
        => user.IsInRole(Constants.Roles.ADMINISTRATORS);
}
