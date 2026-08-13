using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Small helpers shared by the SMS-notification endpoints.
/// </summary>
internal static class EndpointHelpers
{
    /// <summary>The caller's identity, taken from the token's name claim (null if unauthenticated).</summary>
    public static string? GetBuyerId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name);

    /// <summary>
    /// A placeholder shipping address. The order/notification surface does not collect an address, but the
    /// existing Order aggregate requires one; a caller-supplied address is used when present.
    /// </summary>
    public static Address DefaultShipToAddress() =>
        new("Not provided", "Not provided", "Not provided", "Not provided", "00000");
}
