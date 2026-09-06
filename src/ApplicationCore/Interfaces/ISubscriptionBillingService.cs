using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// This runs alongside — not instead of — the one-time Catalog/Basket/Order flow.
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
    /// Idempotent: replaying the same intent returns the existing subscription with
    /// <see cref="SubscribeResult.AlreadyExisted"/> set, instead of enrolling the shopper twice.
    /// </remarks>
    /// <param name="planHandle">Plan to subscribe to, or <c>null</c> to use the configured default plan.</param>
    /// <param name="idempotencyKey">
    /// Optional caller-supplied key scoping the idempotency of this request. When omitted, the intent is
    /// keyed on the subscriber and plan, so a shopper cannot hold two concurrent subscriptions to one plan.
    /// </param>
    Task<SubscribeResult> SubscribeAsync(
        Subscriber subscriber,
        string? planHandle,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription on file for <paramref name="subscriber"/>, newest first.
    /// Returns an empty list when the shopper has no billing customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        Subscriber subscriber,
        CancellationToken cancellationToken = default);
}
