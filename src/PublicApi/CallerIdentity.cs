using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Resolves the calling shopper's identity from the validated JWT.</summary>
public static class CallerIdentity
{
    /// <summary>
    /// The buyer id (username) the JWT was issued for. This is the same value used as
    /// <c>Order.BuyerId</c>, so shopper data scopes to it. Null when unauthenticated.
    /// </summary>
    public static string? GetBuyerId(this HttpContext httpContext)
    {
        var user = httpContext.User;
        return user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
    }
}
