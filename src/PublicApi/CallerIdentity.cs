using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Reads the caller's identity from the JWT. The token stores the user name in <see cref="ClaimTypes.Name"/>,
/// which is the same value used as an Order's BuyerId — so it scopes a shopper to their own data.
/// </summary>
public static class CallerIdentity
{
    public static string? GetUserName(this ClaimsPrincipal user) => user?.Identity?.Name;
}
