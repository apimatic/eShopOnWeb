using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Resolves the caller's stable buyer identity from the JWT. The token issued by this project carries the
/// user name as <see cref="ClaimTypes.Name"/>; that is the identity the existing order model keys on.
/// </summary>
public static class CurrentUser
{
    public static string BuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name)
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.Identity?.Name
        ?? throw new System.InvalidOperationException("Authenticated request carries no user identity claim.");
}
