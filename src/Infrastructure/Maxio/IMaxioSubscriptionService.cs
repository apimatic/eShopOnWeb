using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// High-level subscription operations on Maxio Advanced Billing, orchestrating the
/// Billing API through <see cref="IMaxioBillingClient"/> with idempotent semantics:
/// a repeat call (double-click, retry) never creates a second customer or subscription.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Lists all subscribable plans in the configured product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a single plan by product handle, or null if it does not exist.</summary>
    Task<MaxioProduct?> GetPlanAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Billing API customer exists for the given application user, keyed by a
    /// stable reference derived from the username. Idempotent and safe under concurrency.
    /// </summary>
    Task<MaxioCustomer> EnsureCustomerAsync(string userName, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the Billing API customer for the given application user without creating one,
    /// or null if the user has never been enrolled.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByUserAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the customer to the given plan. If the customer already has a live
    /// subscription for that plan, it is returned unchanged instead of creating another.
    /// Returns the subscription and whether this call created it.
    /// </summary>
    Task<(MaxioSubscription Subscription, bool Created)> SubscribeAsync(MaxioCustomer customer, MaxioProduct plan, CancellationToken cancellationToken = default);

    /// <summary>Lists the customer's subscriptions, or an empty list if the customer has none.</summary>
    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(MaxioCustomer customer, CancellationToken cancellationToken = default);
}