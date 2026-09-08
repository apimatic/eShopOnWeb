using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

/// <summary>
/// Application service for the Maxio Advanced Billing subscription capability. Maxio is the
/// system of record; all plan and subscription state is read/written through the Maxio API.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscription plans offered on the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for the shopper and subscribes them to the plan
    /// identified by <paramref name="planHandle"/>. Idempotent: a shopper who already holds an
    /// ongoing subscription to the plan gets that subscription back; concurrent duplicates are
    /// collapsed onto a single subscription.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscriberAccount subscriber, string planHandle, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the shopper's subscriptions in Maxio. Returns an empty list when the shopper has
    /// no Maxio customer yet (no read side-effect is created).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(SubscriberAccount subscriber, CancellationToken cancellationToken);
}
