using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Talks to Maxio Advanced Billing, the billing system of record for eShopOnWeb
/// subscriptions.
/// </summary>
public interface IMaxioBillingClient
{
    /// <summary>Lists the subscribable plans in the configured product family.</summary>
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="buyerReference"/> and enrolls them
    /// in <paramref name="planHandle"/>. Idempotent: if the buyer already has a non-canceled
    /// subscription to that plan, that subscription is returned instead of creating a
    /// duplicate.
    /// </summary>
    Task<MaxioSubscription> SubscribeAsync(string buyerReference, string email, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the buyer. Returns an empty list if the buyer has
    /// no Maxio customer yet (i.e. they have never subscribed).
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForBuyerAsync(string buyerReference, CancellationToken cancellationToken = default);
}
