using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

internal static class BuyerIdentity
{
    public static string RequireBuyerId(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }

        return buyerId;
    }
}
