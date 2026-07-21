using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared "own subscription, or Admin for any subscription" check used by the endpoints that
/// act on a specific subscription id (UC2/UC3/UC4). There is no persisted userId-to-subscription
/// mapping (§8: stateless), so ownership is verified live against the caller's own subscriptions.
/// </summary>
internal static class SubscriptionAccessControl
{
    public static async Task<bool> CanAccessAsync(
        ISubscriptionService subscriptionService,
        string userName,
        bool isAdministrator,
        int subscriptionId)
    {
        if (isAdministrator)
        {
            return true;
        }

        var subscriptions = await subscriptionService.GetSubscriptionsForUserAsync(userName);
        return subscriptions.Any(s => s.Id == subscriptionId);
    }
}
