using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The authenticated shopper's identity, used as the buyer id for orders and saved cards. The
    /// JWT carries it as the name claim; every payment endpoint is [Authorize]d, so it is present.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            // Should never happen behind [Authorize]; guard defensively rather than trust a null.
            throw new System.UnauthorizedAccessException("The caller's identity could not be determined.");
        }
        return buyerId;
    }
}
