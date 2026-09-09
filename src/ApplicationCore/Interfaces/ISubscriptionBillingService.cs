using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing. This is an additive, parallel
/// capability alongside the existing one-time commerce flow. Implementations translate every
/// provider failure into <see cref="SubscriptionBillingException"/> at their boundary.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans available to subscribe to (the configured product family).</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the caller to <paramref name="planHandle"/>. Idempotent: ensures a single Maxio
    /// customer exists for the user and returns an existing live subscription to the plan rather
    /// than creating a duplicate.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the caller's subscriptions. Returns empty when the user has no Maxio customer yet.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
