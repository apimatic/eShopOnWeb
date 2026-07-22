using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves who the caller is acting as. Administrators may act on any subscription; every
/// other caller is confined to their own, which <see cref="ApplicationCore.Interfaces.ISubscriptionService"/>
/// enforces from the returned owner reference.
/// </summary>
public static class SubscriptionCaller
{
    /// <summary>The caller's own eShopOnWeb reference, or <c>null</c> when they are an administrator.</summary>
    public static string? ResolveOwnerBuyerId(ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
            ? null
            : user.Identity?.Name;

    /// <summary>The caller's own eShopOnWeb reference, regardless of role.</summary>
    public static string BuyerId(ClaimsPrincipal user) => user.Identity?.Name ?? string.Empty;
}
