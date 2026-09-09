using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Resolves the caller's shopper identity (the buyer id) from the JWT.</summary>
public static class CallerIdentity
{
    /// <summary>
    /// The buyer id is the authenticated username/email carried in the token — the same value the
    /// app already uses as <c>Order.BuyerId</c>. Endpoints are always behind [Authorize], so a
    /// missing name means a misconfigured token.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name)
        ?? user.Identity?.Name
        ?? string.Empty;
}
