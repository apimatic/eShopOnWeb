using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's username (email), which is what <c>Order.BuyerId</c> and a shopper's data are keyed on.
    /// The token carries it as <see cref="ClaimTypes.Name"/> (mapped from the JWT <c>unique_name</c> claim).
    /// </summary>
    public static string? GetUserName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name)
        ?? principal.FindFirstValue("unique_name")
        ?? principal.Identity?.Name;
}
