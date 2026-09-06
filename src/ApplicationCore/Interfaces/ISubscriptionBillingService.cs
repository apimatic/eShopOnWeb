using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription capability, backed by an external billing system of record.
/// This sits alongside — and is independent of — the one-time Basket/Order checkout flow.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper may subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrols <paramref name="subscriber"/> on <paramref name="planHandle"/>, creating the billing customer
    /// first if needed. Implementations must be idempotent: repeating the call for the same subscriber and
    /// plan returns the existing live subscription rather than creating a second one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription the billing provider holds for <paramref name="subscriber"/>.</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
