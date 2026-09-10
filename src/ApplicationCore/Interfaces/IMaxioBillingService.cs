using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port for recurring-subscription billing backed by Maxio Advanced Billing (the system of record).
/// The eShopOnWeb HTTP layer depends only on this abstraction; the Maxio HTTP client lives in Infrastructure.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>Lists the plans a shopper can subscribe to (the products in the configured Maxio product family).</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the shopper to the given plan. Idempotent: ensures a single Maxio customer exists for the
    /// shopper and, if they already have a live subscription to the plan, returns it instead of creating a duplicate.
    /// </summary>
    /// <param name="subscriber">The authenticated shopper's identity (from their token).</param>
    /// <param name="planHandle">The stable handle of the plan to subscribe to.</param>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's subscriptions as recorded in Maxio. Returns empty when they have no Maxio customer yet.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
