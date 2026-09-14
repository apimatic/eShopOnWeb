using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates eShopOnWeb subscription billing on top of Maxio: ensures a Maxio customer
/// exists for a buyer (idempotently) and enrolls/looks up their subscriptions.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<MaxioProduct>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="buyerReference"/> and enrolls them in
    /// <paramref name="planHandle"/>. Idempotent: a repeat call for a plan the buyer is already
    /// subscribed to (in a live state) returns the existing subscription instead of creating a new one.
    /// </summary>
    Task<MaxioSubscription> SubscribeAsync(string buyerReference, string buyerEmail, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Returns an empty list when the buyer has no Maxio customer record yet.</summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForBuyerAsync(string buyerReference, CancellationToken cancellationToken = default);
}
