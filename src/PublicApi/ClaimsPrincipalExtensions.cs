using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The signed-in shopper's identity, used as the buyer id for orders, contact numbers and
    /// notifications. Taken from the JWT's name claim, never from the request body.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
            throw new InvalidOperationException("The bearer token does not carry a user identity.");
        return buyerId;
    }
}
