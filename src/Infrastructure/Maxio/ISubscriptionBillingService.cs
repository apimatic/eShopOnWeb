using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the subscription billing flows (plan catalog, subscribe, list my subscriptions)
/// on top of the Maxio Advanced Billing API.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the purchasable plans (non-archived products) in the configured product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently subscribes a user to a plan: ensures a Maxio customer exists for the user
    /// (keyed by <paramref name="customerReference"/>) and creates the subscription. If the user
    /// already holds an open subscription to the same plan, that subscription is returned instead
    /// of creating a duplicate.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string customerReference, string email, string firstName, string lastName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions for the user; empty when the user has no Maxio customer yet.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}

/// <param name="Subscription">The resulting (newly created or pre-existing) subscription.</param>
/// <param name="Created">True when a new subscription was created; false when an existing one was returned.</param>
public record SubscribeResult(MaxioSubscription Subscription, bool Created);
