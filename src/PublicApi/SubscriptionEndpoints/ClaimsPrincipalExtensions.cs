using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Resolves whose subscription an operation acts on. Callers act on their own subscriptions;
    /// only an administrator may name another user, which keeps the customer and admin surfaces of
    /// UC2/UC4 on one endpoint without letting a customer reach someone else's billing.
    /// </summary>
    public static string ResolveUserReference(this ClaimsPrincipal user, string? requestedUserReference)
    {
        var callerReference = user.Identity?.Name ?? string.Empty;

        if (string.IsNullOrWhiteSpace(requestedUserReference))
        {
            return callerReference;
        }

        if (user.IsInRole(Constants.Roles.ADMINISTRATORS))
        {
            return requestedUserReference;
        }

        return callerReference;
    }
}
