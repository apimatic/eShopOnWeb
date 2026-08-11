using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's identity as used for buyer ownership. It comes from the JWT (the name claim) — never
    /// from the request body — so a shopper can only ever act on their own data.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        return string.IsNullOrEmpty(buyerId)
            ? throw new System.InvalidOperationException("The authenticated token does not carry a name claim.")
            : buyerId;
    }
}
