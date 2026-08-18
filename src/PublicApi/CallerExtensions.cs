using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Helpers for reading the caller's identity from the validated JWT.</summary>
public static class CallerExtensions
{
    /// <summary>
    /// The signed-in shopper's identity, taken from the token's name claim. This is the value used as a
    /// buyer id throughout the app.
    /// </summary>
    public static string? CallerId(this ClaimsPrincipal user) =>
        user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
}
