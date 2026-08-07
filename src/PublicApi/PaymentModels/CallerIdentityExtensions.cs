using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

public static class CallerIdentityExtensions
{
    /// <summary>
    /// Returns the buyer id (username/email) carried by the JWT, or null if unauthenticated. This is
    /// the same value used as <c>Order.BuyerId</c>, so it ties API callers to their orders and cards.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>Reads an integer route value, or null when absent/unparseable.</summary>
    public static int? GetRouteInt(this HttpContext http, string key)
        => http.Request.RouteValues.TryGetValue(key, out var value)
            && int.TryParse(value?.ToString(), out var parsed)
            ? parsed
            : null;
}
