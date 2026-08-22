using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ClaimsPrincipalExtensions
{
    public static string RequireBuyerId(this ClaimsPrincipal? user)
    {
        var name = user?.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }

        return name;
    }
}
