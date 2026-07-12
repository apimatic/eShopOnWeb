using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointHelpers
{
    public static string RequireUserReference(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException("Authenticated request had no identity name claim.");
        }

        return name;
    }

    public static bool IsAdmin(ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

    /// <summary>Null (admin bypass, any subscription) if the caller is an admin; otherwise the caller's own reference.</summary>
    public static string? ResolveOwnerReference(ClaimsPrincipal user) => IsAdmin(user) ? null : RequireUserReference(user);
}
