using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Reads the caller's identity from the validated JWT. The identity always comes from the token, never
/// from the request body, so a shopper can only ever act on their own data.
/// </summary>
public static class CallerIdentity
{
    public static string? GetUserName(ClaimsPrincipal user) =>
        user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
}
