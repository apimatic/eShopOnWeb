using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Decides whose subscriptions a caller is allowed to act on.
/// </summary>
public static class SubscriptionCaller
{
    /// <summary>
    /// Returns the reference the operation must be restricted to, or null for an administrator, who may
    /// act on any subscription. A non-administrator is always restricted to their own identity.
    /// </summary>
    public static string? ResolveOwnerReference(ClaimsPrincipal user)
    {
        return user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
            ? null
            : user.Identity?.Name;
    }
}
