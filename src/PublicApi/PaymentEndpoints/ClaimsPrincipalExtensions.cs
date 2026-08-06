using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The shopper identity carried by the JWT. Orders and saved cards are scoped to this value,
    /// so it must come from the token and never from the request body.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
    {
        return user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
