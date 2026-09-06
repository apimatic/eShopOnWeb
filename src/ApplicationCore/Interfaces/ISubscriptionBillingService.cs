using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, delegated to an external billing system of record.
/// </summary>
/// <remarks>
/// This capability runs in parallel to the one-time Catalog/Basket/Order flow; it does not replace it.
/// </remarks>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper may subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="subscriber"/> on the plan identified by <paramref name="planHandle"/>,
    /// creating the billing-system customer record first if it does not exist yet.
    /// </summary>
    /// <remarks>
    /// The operation is idempotent per (subscriber, plan): repeating it while a live subscription exists
    /// returns that subscription with <see cref="SubscribeResult.AlreadyExisted"/> set instead of enrolling twice.
    /// </remarks>
    /// <exception cref="Exceptions.SubscriptionPlanNotFoundException">
    /// The plan handle is not published in the configured product family.
    /// </exception>
    /// <exception cref="Exceptions.PaymentMethodRequiredException">
    /// The plan cannot be subscribed to without capturing a payment instrument.
    /// </exception>
    /// <exception cref="Exceptions.BillingProviderException">The billing system rejected or failed the request.</exception>
    Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription held by <paramref name="subscriber"/>, newest first.
    /// Returns an empty list when the shopper has no billing-system customer record yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
