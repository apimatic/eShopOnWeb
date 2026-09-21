using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Reads the caller's identity from the JWT. The username claim (<see cref="ClaimTypes.Name"/>) is the same
/// value stored as <c>Order.BuyerId</c> / <c>ContactNumber.BuyerId</c>, so it scopes every shopper action to
/// the caller's own data.
/// </summary>
public static class CallerIdentity
{
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}
