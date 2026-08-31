using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's identity, taken from the JWT (ClaimTypes.Name).</summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException("The caller's token carries no name claim.");
        }
        return name;
    }
}
