using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Port to the recurring-subscription billing system of record (Maxio Advanced Billing).
/// The presentation layer talks only to this abstraction; the concrete adapter lives in
/// Infrastructure. Implementations must be idempotent with respect to customer provisioning
/// so repeated calls for the same <see cref="SubscriberIdentity"/> never create duplicates.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper can subscribe to, from the configured product family.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="subscriber"/> (find-or-create,
    /// idempotent on the subscriber reference) and enrols them in the plan identified by
    /// <paramref name="planHandle"/>, returning the resulting subscription. If the subscriber
    /// is already actively subscribed to that plan, the existing subscription is returned
    /// rather than a duplicate being created.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions belonging to the given subscriber, newest first.</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
