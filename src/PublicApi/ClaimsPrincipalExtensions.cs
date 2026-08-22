using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    public static string RequireBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new UnauthorizedAccessException("The caller identity is missing from the token.");
        }

        return buyerId;
    }
}
