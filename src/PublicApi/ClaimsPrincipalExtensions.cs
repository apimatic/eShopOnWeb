using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The shopper's identity, taken from the JWT (the token carries the username as its name claim).</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) => user.Identity?.Name;
}
