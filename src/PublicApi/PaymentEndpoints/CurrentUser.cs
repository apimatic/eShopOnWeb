using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Helpers for reading the caller's identity from the JWT.</summary>
public static class CurrentUser
{
    /// <summary>
    /// The shopper's identity used as the order/card owner. The token carries the user name as
    /// <see cref="ClaimTypes.Name"/> (see IdentityTokenClaimService), which is also how orders are keyed.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}
