using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's identity, taken from the JWT (the name claim). This is the scoping key for a
    /// shopper's own contact numbers, orders and notifications.
    /// </summary>
    public static string? GetCallerId(this ClaimsPrincipal principal)
        => principal.Identity?.Name;
}
