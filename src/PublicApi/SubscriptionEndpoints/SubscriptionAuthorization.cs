using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Ownership checks for subscription-scoped endpoints.
/// </summary>
internal static class SubscriptionAuthorization
{
    /// <summary>
    /// Determines whether the caller may act on a specific subscription. Administrators may act on
    /// any subscription; every other caller only on one they own.
    /// </summary>
    /// <remarks>
    /// Resolves ownership from the caller's own subscriptions rather than trusting anything in the
    /// request, so a subscription id belonging to somebody else can never be acted on.
    /// </remarks>
    public static async Task<bool> CanActOnSubscriptionAsync(
        ClaimsPrincipal user,
        int subscriptionId,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (SubscriptionCaller.IsAdministrator(user))
        {
            return true;
        }

        var own = SubscriptionCaller.ResolveUserReference(user, requestedReference: null);
        if (string.IsNullOrWhiteSpace(own))
        {
            return false;
        }

        var subscriptions = await subscriptionService.ListSubscriptionsAsync(own, cancellationToken);

        foreach (var subscription in subscriptions)
        {
            if (subscription.Id == subscriptionId)
            {
                return true;
            }
        }

        return false;
    }
}
