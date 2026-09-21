using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, abstracted from the billing provider. Implementations talk to
/// the billing system of record; failures surface as
/// <see cref="Exceptions.BillingException"/> so callers have a single failure type to map.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans a shopper can subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the caller to a plan. Idempotent: ensures a single billing customer exists for
    /// the caller and does not create a second live subscription to the same plan, so a repeated
    /// (e.g. double-clicked) request returns the existing subscription rather than duplicating it.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the caller's subscriptions. Returns an empty list when the caller has no billing
    /// customer yet (a read never creates one).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}
