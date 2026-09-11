using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public static class CallerIdentity
{
    /// <summary>
    /// The shopper's identity, taken from the JWT (the Name claim). Used as the order/card owner so
    /// every shopper-scoped action acts only on the caller's own data.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
        => user.Identity?.Name
           ?? user.FindFirstValue(ClaimTypes.Name)
           ?? user.FindFirstValue("unique_name");
}
