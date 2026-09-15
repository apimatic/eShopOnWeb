using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Helpers for reading the caller's identity from the validated JWT. The buyer id used throughout the
/// order/notification model is the token's name claim.
/// </summary>
public static class CallerIdentity
{
    /// <summary>The signed-in shopper's id (the JWT name claim), or null when unauthenticated.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user?.FindFirstValue(ClaimTypes.Name);
}
