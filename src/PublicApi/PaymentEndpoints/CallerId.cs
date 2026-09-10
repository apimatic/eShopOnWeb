using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class CallerId
{
    /// <summary>
    /// The caller's buyer id, taken from the JWT (ClaimTypes.Name is the username the token was
    /// issued for). This is the same value the storefront uses as an Order's BuyerId.
    /// </summary>
    public static string? BuyerId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}
