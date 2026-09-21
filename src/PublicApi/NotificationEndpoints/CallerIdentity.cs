using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Resolves the caller's shopper identity from the JWT. The buyer id is the token's name claim,
/// matching how the rest of eShopOnWeb keys orders by buyer.</summary>
public static class CallerIdentity
{
    public static string? GetBuyerId(ClaimsPrincipal? user) =>
        user?.FindFirstValue(ClaimTypes.Name);
}
