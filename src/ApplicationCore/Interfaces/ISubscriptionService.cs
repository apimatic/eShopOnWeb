using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription capability, backed by Maxio Advanced Billing as the system of record.
/// This is additive to (and independent of) the one-time Catalog/Basket/Order flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrols the shopper in the given plan. Idempotent: ensures a single Maxio customer exists for the
    /// shopper and does not create a second subscription when a live one to the same plan already exists.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberInfo subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's subscriptions. Returns an empty list if the shopper has no Maxio customer yet.</summary>
    Task<IReadOnlyList<SubscriberSubscription>> GetSubscriptionsAsync(SubscriberInfo subscriber, CancellationToken cancellationToken = default);
}
