using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class CallerIdentity
{
    /// <summary>
    /// The caller's identity comes from the JWT. The token sets <see cref="ClaimTypes.Name"/> to the username
    /// (email), which is what the app uses as the buyer id on orders. Every shopper-scoped operation acts only on
    /// data owned by this value.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("name")
            ?? user.Identity?.Name;

        if (string.IsNullOrEmpty(buyerId))
        {
            // Should not happen behind [Authorize], but never fall back to an empty/shared identity.
            throw new System.UnauthorizedAccessException("The authenticated token does not carry a usable identity.");
        }
        return buyerId;
    }
}
