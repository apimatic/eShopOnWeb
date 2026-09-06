using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// This is additive to the one-time Catalog/Basket/Order flow and shares no state with it.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Plans the shopper may subscribe to, taken from the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>The plan with the given handle, or null when the catalog has no such plan.</summary>
    Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the shopper in a plan, creating the billing customer first if needed.
    /// Idempotent: when a live subscription to the same plan already exists it is returned as-is
    /// with <see cref="SubscriptionEnrollment.AlreadyEnrolled"/> set, and nothing is created.
    /// </summary>
    Task<SubscriptionEnrollment> SubscribeAsync(SubscriberIdentity subscriber, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every subscription belonging to the shopper, newest first.
    /// Returns an empty list when the shopper has never been enrolled.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
