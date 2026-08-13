using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's buyer identity, taken from the token. eShop uses the user name as the
    /// buyer id throughout the order model.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            throw new UnauthorizedAccessException("The request does not carry a user identity.");
        }
        return buyerId;
    }
}
