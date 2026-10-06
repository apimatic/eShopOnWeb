using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// High-level subscription capability backed by Maxio Advanced Billing.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Lists the subscription plans available in the configured product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the subscriber and subscribes them to the given plan.
    /// Idempotent: repeated calls for the same user and plan return the existing subscription.
    /// </summary>
    Task<MaxioSubscription> SubscribeAsync(MaxioSubscriber subscriber, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions that belong to the given subscriber.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListMySubscriptionsAsync(MaxioSubscriber subscriber, CancellationToken cancellationToken = default);
}
