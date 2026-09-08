using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates recurring-subscription billing against the external billing
/// system of record (Maxio Advanced Billing).
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the subscription plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDetails>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently enrolls the buyer into the plan identified by <paramref name="productHandle"/>.
    /// Guarantees a billing-system customer exists for the buyer, and never creates a duplicate
    /// subscription for the same buyer + plan.
    /// </summary>
    Task<SubscriptionDetails> SubscribeAsync(string buyerId, string productHandle, string email,
        string displayName, CancellationToken cancellationToken = default);

    /// <summary>Lists the buyer's subscriptions, refreshed from the billing system of record.</summary>
    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsForUserAsync(string buyerId,
        CancellationToken cancellationToken = default);
}
