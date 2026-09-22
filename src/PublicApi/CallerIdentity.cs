using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Resolves the calling shopper's identity from the JWT. The identity is the name claim the token
/// was issued with, which is also how the existing order flow keys a buyer.
/// </summary>
public static class CallerIdentity
{
    public static string? GetBuyerId(ClaimsPrincipal? user) =>
        user?.FindFirstValue(ClaimTypes.Name) ?? user?.Identity?.Name;
}
