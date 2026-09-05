using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Reads and enrolls subscriptions against Maxio Advanced Billing, the system of record for
/// recurring billing. This is additive to, and independent of, the existing basket/order flow.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Lists the plans available to subscribe to, for the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="buyerEmail"/> and enrolls them in the
    /// plan identified by <paramref name="planHandle"/>. Idempotent: if the buyer already has a
    /// live subscription to that plan, the existing subscription is returned rather than creating
    /// a duplicate.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string buyerEmail, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions belonging to the buyer. Empty if they have never subscribed.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForBuyerAsync(string buyerEmail, CancellationToken cancellationToken = default);
}
