using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Reads the caller's identity from the validated JWT. The username (the <see cref="ClaimTypes.Name"/>
/// claim minted at authentication) is the same value the order/bill model uses as the buyer id, so it is
/// what scopes a shopper to their own orders and bills.
/// </summary>
public static class CallerIdentity
{
    public static string? GetUserName(this ClaimsPrincipal? principal) =>
        principal?.FindFirstValue(ClaimTypes.Name) ?? principal?.Identity?.Name;
}
