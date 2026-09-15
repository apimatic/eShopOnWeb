using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentApi;

public static class CallerExtensions
{
    /// <summary>
    /// The identity of the caller, taken from the JWT (the name claim). This is the value used as
    /// an order's / saved card's <c>BuyerId</c>, so every shopper acts only on their own data.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            // The [Authorize] attribute should prevent this; guard anyway.
            throw new PaymentValidationException("The request token does not identify a user.");
        }
        return buyerId;
    }
}
