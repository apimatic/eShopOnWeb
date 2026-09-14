using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, delegated to an external billing system of record.
/// <para>
/// This runs alongside the one-time Catalog/Basket/Order flow and shares nothing with it:
/// no subscription state is persisted locally, the billing system is always the source of truth.
/// </para>
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Plans the shopper can subscribe to, taken from the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the shopper on a plan, creating their billing customer first if needed.
    /// The operation is idempotent: repeating it never yields a second customer or a second
    /// live subscription to the same plan.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every subscription the shopper holds, newest first. Empty when they have no billing
    /// customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        BillingIdentity identity,
        CancellationToken cancellationToken = default);
}
