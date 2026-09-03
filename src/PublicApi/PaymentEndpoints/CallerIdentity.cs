using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public static class CallerIdentity
{
    /// <summary>
    /// The shopper's identity, taken from the JWT <see cref="ClaimTypes.Name"/> claim. This is the value
    /// used as an order's BuyerId and to scope saved cards, so a caller can only ever touch their own data.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}
