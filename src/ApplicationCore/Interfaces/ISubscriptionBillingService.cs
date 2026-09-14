using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing. This is the seam the
/// PublicApi endpoints depend on; the concrete implementation lives in Infrastructure and is the
/// only place that talks to the Maxio SDK. Every method translates provider/SDK failures into
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.BillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrols the given shopper in a plan, idempotently: ensures a Maxio customer exists for the
    /// shopper (looked up by <see cref="SubscriberIdentity.Reference"/>, created only if absent),
    /// and returns any existing live subscription for the same plan rather than creating a duplicate.
    /// </summary>
    /// <param name="subscriber">The shopper's stable billing identity.</param>
    /// <param name="planHandle">
    /// The plan (product handle) to subscribe to; when null/blank the configured default plan is used.
    /// </param>
    Task<CustomerSubscription> SubscribeAsync(
        SubscriberIdentity subscriber,
        string? planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the shopper's subscriptions. Returns an empty list when the shopper has no Maxio
    /// customer yet (i.e. has never subscribed).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        string subscriberReference,
        CancellationToken cancellationToken = default);
}
