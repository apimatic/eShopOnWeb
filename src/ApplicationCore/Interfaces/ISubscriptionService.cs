using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic recurring-subscription billing capability. The eShopOnWeb application
/// depends only on this abstraction; the concrete implementation talks to the billing
/// system of record (Maxio Advanced Billing).
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans a shopper can subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the given subscriber (idempotent) and enrolls
    /// them in the requested plan. If the subscriber already has a live subscription to that
    /// plan, the existing one is returned rather than creating a duplicate.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(
        SubscriberInfo subscriber,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriber's subscriptions. Returns empty if they have no billing customer yet.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberInfo subscriber,
        CancellationToken cancellationToken = default);
}
