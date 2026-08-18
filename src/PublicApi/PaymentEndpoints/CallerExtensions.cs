using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class CallerExtensions
{
    /// <summary>
    /// The caller's buyer identity, taken from the JWT (the same <see cref="ClaimTypes.Name"/> claim the
    /// authenticate endpoint issues). Every shopper-scoped endpoint acts only on this buyer's own data.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
            throw new UnauthorizedAccessException("The caller identity is missing from the token.");
        return buyerId;
    }
}
