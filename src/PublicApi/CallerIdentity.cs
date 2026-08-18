using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Helpers for reading the calling shopper's identity from the JWT. The identity is always
/// taken from the token, never from the request body, so a caller can only ever act as
/// themselves.
/// </summary>
public static class CallerIdentity
{
    /// <summary>
    /// The buyer id for the caller — the token's name claim, which matches the value the app uses
    /// as an order's <c>BuyerId</c>. Returns null when there is no authenticated name.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.Name)
               ?? user.Identity?.Name
               ?? user.FindFirstValue("unique_name");
    }
}
