using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Reads the shopper's identity (used as the order/card owner) from the JWT.</summary>
public static class CallerIdentity
{
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        return buyerId ?? string.Empty;
    }
}
