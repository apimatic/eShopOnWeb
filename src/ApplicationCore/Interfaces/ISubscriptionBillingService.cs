using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Domain service for the subscription-billing capability. Orchestrates Maxio so that
/// subscribing is idempotent: a shopper never gets duplicate customers or subscriptions,
/// even on retries/double-clicks.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans available for subscription (non-archived products in the configured product family).</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user and subscribes them to the plan.
    /// If the user already has a live subscription to the same plan, that existing
    /// subscription is returned instead of creating a duplicate.
    /// </summary>
    Task<SubscriptionDetails> SubscribeAsync(string userReference, string email, string? firstName, string? lastName, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the user's subscriptions. Empty when the user has no Maxio customer yet.</summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}
