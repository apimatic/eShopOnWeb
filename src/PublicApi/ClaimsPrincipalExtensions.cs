using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's identity from the JWT (the username they authenticated with).</summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.FindFirst("unique_name")?.Value
            ?? user.FindFirst("name")?.Value
            ?? user.Identity?.Name;

        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException("The access token does not contain a name claim.");
        }
        return name;
    }
}
