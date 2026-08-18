using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Helper for reading the signed-in caller's identity from the JWT. The token carries the
/// username (email) under the name claim, which is exactly the value stored as an order's BuyerId
/// and a contact number's / notification's OwnerId — so shopper-scoped endpoints compare against it.
/// </summary>
internal static class CallerIdentity
{
    public static string? GetCallerId(this IHttpContextAccessor accessor)
        => accessor.HttpContext?.User?.Identity?.Name;
}
