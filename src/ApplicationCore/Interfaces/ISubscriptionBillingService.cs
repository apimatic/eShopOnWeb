using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing. Maxio is the system of
/// record: no subscription state is persisted locally, it is always read live from Maxio.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="buyerId"/> (idempotent) and enrolls
    /// them in <paramref name="planHandle"/>. If the buyer already has a live subscription to
    /// that plan, the existing subscription is returned instead of creating a duplicate.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string buyerId, string email, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}
