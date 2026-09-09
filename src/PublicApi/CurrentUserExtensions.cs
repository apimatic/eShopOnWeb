using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Helpers for resolving the caller's identity from the validated JWT. The token encodes the
/// identity as the name claim (the username/email), which is what the order/basket model uses as the
/// buyer id.
/// </summary>
public static class CurrentUserExtensions
{
    /// <summary>The caller's buyer id (their username/email), or null if the token carries no name.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}
