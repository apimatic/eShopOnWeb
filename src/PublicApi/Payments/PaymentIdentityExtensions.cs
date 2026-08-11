using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Resolves the calling shopper's stable identity from the JWT. The token issued by this API
/// carries the user name as the <see cref="ClaimTypes.Name"/> claim; that is used as the buyer
/// id so a caller only ever sees and acts on their own orders and saved cards.
/// </summary>
public static class PaymentIdentityExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
            throw new System.UnauthorizedAccessException("The caller identity could not be determined from the token.");
        return buyerId;
    }
}
