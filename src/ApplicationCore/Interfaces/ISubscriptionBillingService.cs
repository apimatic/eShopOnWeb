using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, expressed in eShopOnWeb terms. The billing system is the system of
/// record: this application stores no subscription state of its own.
/// </summary>
/// <remarks>
/// Every method throws <see cref="Exceptions.BillingException"/> (and only that) when the billing system
/// cannot satisfy the request; the exception carries the HTTP status the caller should see.
/// </remarks>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans that are currently sellable.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="subscriber"/> in the plan identified by <paramref name="planHandle"/>,
    /// creating the billing customer first if one does not exist yet.
    /// </summary>
    /// <remarks>
    /// Idempotent: a repeated call for the same shopper and plan returns the existing subscription with
    /// <see cref="SubscribeResult.AlreadySubscribed"/> set, rather than creating a second one.
    /// </remarks>
    Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists <paramref name="subscriber"/>'s subscriptions. Returns an empty list when the shopper has no
    /// billing customer record yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
