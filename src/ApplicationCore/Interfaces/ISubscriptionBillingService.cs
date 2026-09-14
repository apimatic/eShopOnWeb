using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, delegated to an external system of record. Implementations must be
/// idempotent: subscribing the same user to the same plan twice yields one customer and one subscription.
/// Every failure is surfaced as
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.BillingProviderException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans currently on offer.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="subscriber"/> on <paramref name="planHandle"/>, creating the billing
    /// customer first if this is their first subscription. Safe to call repeatedly.
    /// </summary>
    /// <param name="planHandle">Plan to subscribe to; when null or empty the configured default plan is used.</param>
    Task<SubscribeResult> SubscribeAsync(BillingSubscriber subscriber, string? planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriber's subscriptions. Returns an empty collection when they have never subscribed.
    /// The lookup is re-derived from <paramref name="subscriber"/> on every call, so it survives a restart
    /// with no local persistence.
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(BillingSubscriber subscriber,
        CancellationToken cancellationToken = default);
}
