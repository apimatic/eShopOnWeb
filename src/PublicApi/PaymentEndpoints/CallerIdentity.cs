using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class CallerIdentity
{
    /// <summary>
    /// The shopper's stable id, taken from the JWT (never from the request body). Matches the buyer id
    /// the storefront uses for orders, so payments and orders line up per shopper.
    /// </summary>
    public static string BuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name ?? string.Empty;
}
