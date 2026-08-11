using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Resolves the caller's identity from the JWT. The buyer id used throughout the payment flow is
/// the token's name claim, so every shopper-scoped operation acts only on the caller's own data.
/// </summary>
public static class CurrentUser
{
    public static string BuyerId(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
        {
            throw new ForbiddenAccessException("The access token does not identify a shopper.");
        }
        return buyerId;
    }
}
