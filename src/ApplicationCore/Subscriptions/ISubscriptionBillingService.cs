using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// This capability runs alongside - and independently of - the one-time basket/order flow.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper may subscribe to.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="subscriber"/> on the plan identified by <paramref name="planHandle"/>.
    /// Idempotent: repeating the call returns the shopper's existing subscription rather than
    /// creating a second customer or a second subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription held by <paramref name="subscriber"/>, newest first.</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
