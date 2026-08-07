using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class CurrentUserExtensions
{
    /// <summary>
    /// The shopper's identity (username/email) taken from the JWT. This is the value the app uses as the
    /// buyer id on orders and saved cards. Returns null when unauthenticated.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) => user.Identity?.Name;
}
