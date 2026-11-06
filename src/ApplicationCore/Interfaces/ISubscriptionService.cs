using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<PlanListResult> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>Idempotent: repeating the call for the same user and plan returns the same subscription.</summary>
    Task<SubscriptionInfo> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken);

    Task<SubscriptionListResult> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
