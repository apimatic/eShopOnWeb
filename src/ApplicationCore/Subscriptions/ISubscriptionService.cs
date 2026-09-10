using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Application service for the subscription-billing capability. Orchestrates the Maxio gateway to
/// deliver the hero flow (subscribe) idempotently, plus plan browsing and subscription listing.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Returns the plans a shopper can subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the user to a plan. Ensures a Maxio customer exists for the user (idempotent by
    /// reference) and reuses an existing live subscription to the same plan rather than creating a
    /// duplicate, so a double-click never produces two customers or two subscriptions.
    /// </summary>
    Task<SubscriptionResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the user's subscriptions. If no Maxio customer exists for the reference yet
    /// (the user has never subscribed), an empty list is returned.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
