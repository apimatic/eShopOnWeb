using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The buyer identity for the caller, taken from the JWT (never from the request body). Matches the
    /// username the rest of the app uses as an order's <c>BuyerId</c>.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user) =>
        user.Identity?.Name
        ?? user.FindFirstValue(ClaimTypes.Name)
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? string.Empty;
}
