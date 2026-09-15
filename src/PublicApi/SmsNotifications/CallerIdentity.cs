using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>Helpers for reading the authenticated caller's identity from the JWT.</summary>
public static class CallerIdentity
{
    /// <summary>
    /// The caller's shopper id (their username, carried as the name claim). Every shopper-scoped endpoint
    /// acts only on data owned by this value.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
}
