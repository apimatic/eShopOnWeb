using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Helpers for reading the caller's identity from the validated JWT. The caller's identity is the
/// only source of a shopper's <c>BuyerId</c>; it is never taken from a request body.
/// </summary>
public static class CurrentUser
{
    /// <summary>The signed-in user's name (email), used as the BuyerId. Null if unauthenticated.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return null;
        }

        return user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
    }
}
