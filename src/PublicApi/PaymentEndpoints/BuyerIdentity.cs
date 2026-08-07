using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Resolves the calling shopper's identity from the JWT. Every payment and order operation is scoped
/// to this value so one shopper can never see, use or act on another's data.
/// </summary>
public static class BuyerIdentity
{
    public static string? GetBuyerId(this ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return null;
        }

        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            buyerId = user.FindFirstValue(ClaimTypes.Name);
        }
        return string.IsNullOrEmpty(buyerId) ? null : buyerId;
    }
}
