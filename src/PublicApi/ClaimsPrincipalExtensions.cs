using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Reads the caller's identity from the JWT. The buyer id is the username/email claim, matching
/// <c>Order.BuyerId</c> and <c>ContactNumber.BuyerId</c> so shopper-scoped data lines up with the token.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name);

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(Constants.Roles.ADMINISTRATORS);
}
