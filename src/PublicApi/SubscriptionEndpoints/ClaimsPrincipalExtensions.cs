using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The stable per-user reference used as the Maxio customer/subscription idempotency key.
    /// </summary>
    public static string? GetUserReference(this ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.Name)?.Value
           ?? user.FindFirst("unique_name")?.Value
           ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? user.FindFirst("sub")?.Value
           ?? user.Identity?.Name;
}
