using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, as eShopOnWeb needs it. The billing system of record lives outside this
/// application; this is the only contract the rest of the app is allowed to depend on.
/// </summary>
/// <remarks>
/// Every member throws <see cref="Exceptions.BillingException"/> — and nothing else — for any provider,
/// transport, or configuration failure.
/// </remarks>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans on offer, in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="customer"/> and enrolls them in
    /// <paramref name="planHandle"/>.
    /// </summary>
    /// <remarks>
    /// Idempotent per (customer, plan): concurrent or repeated calls return the existing live subscription
    /// instead of creating a second one.
    /// </remarks>
    Task<SubscribeResult> SubscribeAsync(
        BillingCustomerIdentity customer,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to <paramref name="customer"/>. A shopper who has never subscribed
    /// has no billing customer at all; that is an empty list, not a failure.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        BillingCustomerIdentity customer,
        CancellationToken cancellationToken = default);
}
