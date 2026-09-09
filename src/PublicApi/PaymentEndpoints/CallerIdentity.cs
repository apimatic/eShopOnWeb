using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Resolves the shopper/operator identity from the JWT. The buyer id used across the order and
/// saved-card flows is the token's name claim, so a caller can only ever act on their own data.
/// </summary>
public static class CallerIdentity
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
            throw new PaymentOperationException("The bearer token does not identify a shopper.", 401);
        return buyerId;
    }
}
