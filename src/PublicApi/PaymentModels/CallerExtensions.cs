using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

public static class CallerExtensions
{
    /// <summary>
    /// The caller's shopper identity, taken from the JWT name claim. Orders and saved cards are
    /// scoped to this value so a caller only ever sees and acts on their own data.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}
