using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Helpers for reading the caller's identity out of the validated JWT. eShop uses the user name
/// (email) as the buyer id throughout the order/basket model, so that is what identifies a shopper.
/// </summary>
public static class CallerExtensions
{
    /// <summary>The signed-in shopper's identity (buyer id), taken from the token — never from request input.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}
