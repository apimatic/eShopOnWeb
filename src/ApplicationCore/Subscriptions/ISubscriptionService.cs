using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public interface ISubscriptionService
{
    Task<SubscriptionPlanListing> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>Idempotent per user: a second call for the same plan returns the existing subscription.</summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken);
}
