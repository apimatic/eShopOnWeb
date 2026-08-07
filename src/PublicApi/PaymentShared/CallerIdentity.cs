using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentShared;

/// <summary>Reads the authenticated shopper's identity (the buyer id) from the JWT principal.</summary>
public static class CallerIdentity
{
    /// <summary>
    /// The buyer id is the token's name claim (the shopper's username), matching how the rest of the
    /// app identifies buyers on orders and baskets.
    /// </summary>
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            // Endpoints are [Authorize]d, so this should never happen; guard defensively.
            throw new System.InvalidOperationException("The authenticated user has no identity claim.");
        }
        return buyerId;
    }
}
