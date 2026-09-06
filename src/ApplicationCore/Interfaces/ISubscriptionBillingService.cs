using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, with an external provider as the system of record.
/// <para>
/// Implementations own every provider detail; nothing provider-specific crosses this boundary.
/// Every method either succeeds or throws
/// <see cref="Exceptions.BillingProviderException"/> (or, for the subscribe conflict,
/// <see cref="Exceptions.SubscriptionConflictException"/>) — transport failures, provider errors and
/// unreadable responses are all translated by the implementation.
/// </para>
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper may subscribe to, newest catalog state first-hand from the provider.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes <paramref name="subscriber"/> to the plan identified by <paramref name="planHandle"/>.
    /// <para>
    /// Idempotent: ensures exactly one provider customer exists for the shopper, and returns the existing
    /// subscription (with <see cref="SubscribeOutcome.AlreadySubscribed"/>) instead of creating a second
    /// one when the shopper is already enrolled in that plan.
    /// </para>
    /// </summary>
    /// <exception cref="Exceptions.SubscriptionConflictException">
    /// The shopper already has a live subscription to a different plan.
    /// </exception>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription the provider holds for <paramref name="subscriber"/>.
    /// Returns an empty list when the shopper has never been enrolled.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
