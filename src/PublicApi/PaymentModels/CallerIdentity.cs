using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>Reads the caller's identity (the order/card owner) from the JWT.</summary>
public static class CallerIdentity
{
    /// <summary>
    /// The shopper id used as the order/card owner. In eShopOnWeb this is the username (email), carried as the
    /// token's Name claim. Endpoints are gated by <c>[Authorize]</c>, so an authenticated caller always has one.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
            throw new System.UnauthorizedAccessException("The bearer token does not identify a user.");
        return buyerId;
    }
}
