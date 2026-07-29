using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port for recurring-subscription billing. Implemented in Infrastructure against a concrete
/// billing system (Maxio Advanced Billing). Kept provider-agnostic so the application core and
/// API never depend on billing-system specifics.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper can subscribe to (from the configured product family).</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given user to a plan. Idempotent: ensures a single billing customer exists
    /// for the user and returns an existing live subscription to the same plan instead of creating
    /// a duplicate, so a double-click never creates two customers or two subscriptions.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(BillingCustomerInfo customer, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the given user's subscriptions. Returns an empty collection if the user has no billing customer yet.</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(BillingCustomerInfo customer, CancellationToken cancellationToken = default);
}
