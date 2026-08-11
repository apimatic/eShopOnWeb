using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public static class CurrentUser
{
    /// <summary>
    /// The buyer identity for the authenticated caller. eShopOnWeb uses the username (email)
    /// as the order/basket BuyerId, and the JWT carries it as the Name claim.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
        {
            // Should never happen behind [Authorize]; guards against a malformed token.
            throw new System.UnauthorizedAccessException("The token does not carry a user identity.");
        }
        return buyerId;
    }
}
