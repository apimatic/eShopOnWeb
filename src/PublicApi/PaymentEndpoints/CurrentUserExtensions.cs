using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Resolves the caller's identity from the JWT. Orders and saved cards are scoped to this value, so a
/// caller can only ever see or act on their own data — the identity comes from the token, never the body.
/// </summary>
public static class CurrentUserExtensions
{
    public static string? GetBuyerId(this ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.Name)
            ?? user.Identity?.Name;
    }
}
