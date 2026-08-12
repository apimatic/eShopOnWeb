using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The identity username carried in the JWT (the <see cref="ClaimTypes.Name"/> claim). This is the
    /// order/notification BuyerId, and the value every shopper-scoped endpoint filters on so a caller
    /// only ever acts on their own data.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name)
           ?? user.Identity?.Name
           ?? string.Empty;
}
