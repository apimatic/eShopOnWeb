using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Helpers for resolving the authenticated caller from the JWT.</summary>
public static class CallerExtensions
{
    /// <summary>
    /// The caller's identity (the JWT <see cref="ClaimTypes.Name"/> claim), which matches
    /// <c>Order.BuyerId</c> / <c>ContactNumber.BuyerId</c>. Returns null when unauthenticated.
    /// </summary>
    public static string? GetUserName(this ClaimsPrincipal user) => user.Identity?.Name;
}
