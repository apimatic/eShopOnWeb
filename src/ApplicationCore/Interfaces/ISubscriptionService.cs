using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing operations backed by the billing system of record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available for purchase.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given user to a plan. Idempotent: ensures the billing customer
    /// exists (creating it if needed) and returns the existing subscription when the
    /// user already holds an active one for the same plan.
    /// </summary>
    Task<SubscriptionDetails> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the given user's subscriptions. Returns an empty list when the user has
    /// no billing customer record yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string username, CancellationToken cancellationToken = default);
}
