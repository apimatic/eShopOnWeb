using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Reads the caller's buyer identity from the JWT. Throughout eShopOnWeb the username (the
/// <see cref="ClaimTypes.Name"/> claim) is the buyerId used to scope baskets and orders.
/// </summary>
public static class CallerIdentity
{
    public static string BuyerId(this HttpContext context)
    {
        var name = context.User?.Identity?.Name
            ?? context.User?.FindFirstValue(ClaimTypes.Name);
        return name ?? string.Empty;
    }
}
