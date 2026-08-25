using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by an external billing system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans available for purchase.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes a shopper to a plan. Idempotent: ensures a billing customer exists for the
    /// shopper and returns the existing active subscription if the shopper is already
    /// subscribed to the plan instead of creating a duplicate.
    /// </summary>
    Task<SubscriptionDetails> SubscribeAsync(string buyerId, string buyerEmail, string productHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the shopper's subscriptions. Returns an empty list when the shopper has no
    /// billing customer record yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string buyerId, string buyerEmail,
        CancellationToken cancellationToken = default);
}
