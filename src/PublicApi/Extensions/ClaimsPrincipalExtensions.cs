using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's buyer identity, taken from the token's name claim (never from the request).
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.FindFirst("name")?.Value
            ?? user.Identity?.Name;

        if (string.IsNullOrEmpty(buyerId))
        {
            throw new UnauthorizedAccessException("The token carries no name claim.");
        }

        return buyerId;
    }
}
