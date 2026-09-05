using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the "Subscribe" flow against Maxio: listing plans, idempotently ensuring a
/// Maxio customer exists for an eShopOnWeb user, and enrolling that user in a plan without
/// creating duplicate customers or subscriptions on repeated calls.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Lists the non-archived plans available for subscription.</summary>
    Task<IReadOnlyList<MaxioProduct>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given buyer (keyed by <paramref name="buyerReference"/>,
    /// the eShopOnWeb username) and enrolls them in the plan identified by <paramref name="planHandle"/>.
    /// If the buyer already has a non-terminal subscription to that plan, that existing subscription
    /// is returned instead of creating a new one.
    /// </summary>
    Task<(MaxioSubscription Subscription, bool Created)> SubscribeAsync(
        string buyerReference,
        string buyerEmail,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the buyer. Returns an empty list if the buyer has
    /// never subscribed (i.e. no Maxio customer exists yet for their reference).
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForBuyerAsync(string buyerReference, CancellationToken cancellationToken = default);
}
