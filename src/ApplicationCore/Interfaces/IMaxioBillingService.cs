using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription billing operations backed by Maxio Advanced Billing,
/// the billing system of record.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscription plans (products) available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes a shopper to a plan. Idempotent: ensures exactly one Maxio customer per
    /// eShopOnWeb user and returns the existing live subscription if the shopper is already
    /// subscribed to the same plan.
    /// </summary>
    Task<SubscriptionDetails> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions the shopper has in Maxio. Returns an empty list when the
    /// shopper has no Maxio customer record yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(string userId, string email, CancellationToken cancellationToken = default);
}
