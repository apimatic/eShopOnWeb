using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Resolves the calling shopper's identity from the JWT. The token carries the username as
/// <see cref="ClaimTypes.Name"/>, which is exactly the value the app uses as an order's BuyerId.
/// </summary>
public static class CallerExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal? user)
    {
        var buyerId = user?.FindFirstValue(ClaimTypes.Name) ?? user?.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            throw new PaymentException("The request is not associated with a signed-in shopper.");
        }
        return buyerId;
    }
}
