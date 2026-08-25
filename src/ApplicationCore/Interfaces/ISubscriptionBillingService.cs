using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the subscription billing system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans currently offered to shoppers.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListSubscriptionPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a shopper in a plan. Idempotent: ensures the billing customer exists (never duplicated)
    /// and returns the shopper's existing live subscription to the same plan instead of creating a second one.
    /// </summary>
    Task<ShopperSubscription> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions the shopper (identified by their customer reference) holds.
    /// </summary>
    Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
