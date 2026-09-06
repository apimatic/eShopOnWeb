using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring subscription billing, delegated to an external billing system of record.
/// This capability runs alongside the one-time basket/order flow; it does not replace it.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans a shopper may subscribe to.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="subscriber"/> in the plan identified by <paramref name="planHandle"/>,
    /// creating the billing customer first if one does not exist yet.
    /// </summary>
    /// <remarks>
    /// Idempotent per (subscriber, plan): repeating the call while the shopper already holds a live
    /// subscription to that plan returns the existing subscription instead of creating a second one.
    /// </remarks>
    /// <exception cref="Exceptions.SubscriptionPlanNotFoundException">The plan is not in the configured catalog.</exception>
    /// <exception cref="Exceptions.SubscriptionNotAllowedException">The provider rejected the enrollment.</exception>
    /// <exception cref="Exceptions.SubscriptionBillingUnavailableException">The provider could not be reached.</exception>
    Task<SubscriptionEnrollment> SubscribeAsync(
        Subscriber subscriber,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription held by <paramref name="subscriber"/>, most recent first.
    /// Returns an empty list when the shopper has never subscribed.
    /// </summary>
    Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(
        Subscriber subscriber,
        CancellationToken cancellationToken = default);
}
