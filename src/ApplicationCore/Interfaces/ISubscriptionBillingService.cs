using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the subscription billing system of record (Maxio Advanced Billing).
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently subscribes a shopper to a plan: ensures a billing customer exists for
    /// <paramref name="customerReference"/> (creating it if necessary) and enrolls them in
    /// the plan. If the shopper already has a live subscription to the same plan, the
    /// existing subscription is returned instead of creating a duplicate.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(
        string customerReference,
        string email,
        string displayName,
        string productHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions for the shopper identified by <paramref name="customerReference"/>.
    /// Returns an empty list when the shopper has no billing customer record yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken = default);
}
