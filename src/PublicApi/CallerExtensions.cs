using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class CallerExtensions
{
    /// <summary>
    /// The signed-in shopper's stable identity, taken from the JWT (the name claim), used as the owner of
    /// contact numbers and orders. Every shopper-scoped endpoint acts only on data with this owner.
    /// </summary>
    public static string? ShopperId(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name);
}
