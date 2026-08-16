using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The calling shopper's identity, taken from the JWT (never from the request body). This is the
    /// same value the app uses as an order's and saved card's BuyerId.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal? user)
    {
        var buyerId = user?.Identity?.Name ?? user?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
            throw new PaymentException("The caller's identity could not be determined from the token.");
        return buyerId;
    }

    public static string GetBuyerId(this IHttpContextAccessor accessor) =>
        accessor.HttpContext?.User.GetBuyerId()
        ?? throw new PaymentException("The caller's identity could not be determined from the token.");
}
