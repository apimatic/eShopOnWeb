using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Resolves the calling shopper's identity from the JWT — never from the request body.</summary>
public static class CallerContext
{
    /// <summary>The buyer id (username) from the token, matching eShop's <c>Order.BuyerId</c> convention.</summary>
    public static string? GetBuyerId(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.Name) ?? http.User.Identity?.Name;
}
