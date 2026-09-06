using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription capability, backed by an external billing system of record.
/// This runs alongside - and is independent of - the one-time Basket/Order checkout flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans a shopper may subscribe to, from the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrols <paramref name="subscriber"/> on the plan identified by <paramref name="planHandle"/>,
    /// creating the billing customer first if one does not exist yet.
    /// </summary>
    /// <param name="idempotencyKey">
    /// Optional caller-supplied key. When supplied, the enrolment is idempotent at the billing
    /// provider for that key: repeating the call returns the subscription created by the first one.
    /// When omitted, an existing live subscription to the same plan is returned instead of a new one.
    /// </param>
    /// <exception cref="Exceptions.SubscriptionPlanNotFoundException">
    /// The handle does not match a plan in the configured product family.
    /// </exception>
    Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription held by <paramref name="subscriber"/>, newest first.
    /// Returns an empty list when the shopper has never been enrolled.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
