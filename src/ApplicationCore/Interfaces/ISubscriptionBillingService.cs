using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The boundary between eShopOnWeb and the external recurring-billing provider. Every
/// implementation translates provider failures into
/// <see cref="Exceptions.SubscriptionBillingException"/>, so no provider type escapes this seam.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper may subscribe to, from the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="subscriber"/> in <paramref name="planHandle"/>, creating the billing
    /// customer first if one does not exist yet. Idempotent per shopper and plan: repeating the call
    /// returns the existing live subscription instead of creating a second one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to <paramref name="subscriber"/>. A shopper who has never
    /// subscribed has no billing customer, and is reported as an empty list rather than a failure.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
