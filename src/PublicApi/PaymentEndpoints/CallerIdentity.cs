using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Resolves the caller's identity (buyer id) from the JWT-populated HttpContext.</summary>
public static class CallerIdentity
{
    /// <summary>The signed-in user's name claim (used as the buyer id), or null if unavailable.</summary>
    public static string? GetBuyerId(this IHttpContextAccessor accessor)
        => accessor.HttpContext?.User?.Identity?.Name;
}
