using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record. Implementations keep
/// all provider types behind this interface and raise
/// <see cref="Exceptions.SubscriptionBillingException"/> - and nothing else - on failure.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper can subscribe to, newest catalog state first-hand from the provider.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="subscriber"/> on the plan identified by <paramref name="planHandle"/>.
    /// </summary>
    /// <remarks>
    /// Idempotent: repeated calls for the same subscriber and plan return the existing subscription with
    /// <see cref="SubscriptionEnrollment.AlreadySubscribed"/> set, and never create a second billing
    /// customer or a second subscription.
    /// </remarks>
    Task<SubscriptionEnrollment> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions held by <paramref name="subscriber"/>. Returns an empty list when the
    /// shopper has never been enrolled.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
