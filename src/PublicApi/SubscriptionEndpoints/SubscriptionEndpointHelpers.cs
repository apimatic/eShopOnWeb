using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves which subscription an authenticated caller may act on for the endpoints that are
/// "Customer (own subscription) or Admin (any)" (UC2 usage, UC3 plan change, UC4 lifecycle):
/// the caller's own active subscription by default, or an explicit subscription id when the
/// caller holds the Administrators role. A non-admin who supplies someone else's subscription id
/// is rejected as not-found (rather than forbidden) so the endpoint never confirms whether that id
/// exists.
/// </summary>
internal static class SubscriptionEndpointHelpers
{
    public static async Task<int> ResolveSubscriptionIdAsync(
        ISubscriptionService subscriptionService,
        ClaimsPrincipal user,
        int? requestedSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        var userName = user.Identity?.Name ?? string.Empty;

        if (requestedSubscriptionId is null)
        {
            var mine = await subscriptionService.FindSubscriptionForUserAsync(userName, cancellationToken);
            if (mine is null)
            {
                throw new SubscriptionNotFoundException($"No active subscription was found for '{userName}'.");
            }
            return mine.Id;
        }

        if (user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS))
        {
            return requestedSubscriptionId.Value;
        }

        var owned = await subscriptionService.FindSubscriptionForUserAsync(userName, cancellationToken);
        if (owned is null || owned.Id != requestedSubscriptionId.Value)
        {
            throw new SubscriptionNotFoundException($"No active subscription {requestedSubscriptionId} was found for '{userName}'.");
        }
        return owned.Id;
    }
}
