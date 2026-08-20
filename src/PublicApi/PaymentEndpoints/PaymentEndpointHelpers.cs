using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class PaymentEndpointHelpers
{
    /// <summary>The caller's identity (username) taken from the JWT — the buyer key used across the app.</summary>
    public static string? GetBuyerId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}
