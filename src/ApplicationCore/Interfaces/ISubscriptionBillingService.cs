using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application service that fronts Maxio Advanced Billing for the eShopOnWeb
/// subscription capability (browse plans, subscribe idempotently, list my subscriptions).
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the currently purchasable plans from the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the subscriber exists as a Maxio customer (idempotently) and subscribes them
    /// to the given plan. Safe to call repeatedly: a second call for the same subscriber and
    /// plan returns the already-active subscription instead of creating a duplicate.
    /// </summary>
    /// <param name="subscriberKey">
    /// The unique, stable identifier of the signed-in shopper from the caller's identity
    /// (e.g. the token name claim). Also used as the Maxio customer reference.
    /// </param>
    /// <param name="planHandle">The Maxio product handle (e.g. <c>eshop-pro</c>).</param>
    Task<SubscribeResult> SubscribeAsync(string subscriberKey, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the Maxio subscriptions currently associated with the subscriber.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetailsDto>> ListSubscriptionsAsync(string subscriberKey, CancellationToken cancellationToken = default);
}
