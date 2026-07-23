using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves which eShopOnWeb user a subscription request acts on. A caller always acts on their own
/// subscription unless they are an administrator, who may name any user (UC2 and UC4 allow an admin
/// actor across users).
/// </summary>
internal static class SubscriptionActor
{
    public static bool TryResolve(ClaimsPrincipal principal, string? onBehalfOfUserName, out string userName)
    {
        userName = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(onBehalfOfUserName)
            || string.Equals(onBehalfOfUserName, userName, System.StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(userName);
        }

        if (!principal.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS))
        {
            return false;
        }

        userName = onBehalfOfUserName!;
        return true;
    }
}
