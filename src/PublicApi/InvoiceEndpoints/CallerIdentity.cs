using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Helpers for reading the authenticated caller from the JWT. The caller's identity always comes
/// from the token — never from anything the caller restates in a request body — so one shopper can
/// never act as another.
/// </summary>
public static class CallerIdentity
{
    /// <summary>The shopper's user name (the <c>ClaimTypes.Name</c> claim minted into the token).</summary>
    public static string? GetBuyerId(this ClaimsPrincipal? user) =>
        user?.FindFirstValue(ClaimTypes.Name) ?? user?.Identity?.Name;
}
