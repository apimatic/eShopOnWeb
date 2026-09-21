using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing against the billing system of record (Maxio Advanced Billing).
/// Implementations own all provider interaction; callers see only transport-neutral DTOs and
/// <see cref="Exceptions.SubscriptionBillingException"/> on failure.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given user to the plan identified by <paramref name="planHandle"/>. Ensures a
    /// Maxio customer exists for the user (idempotent) and enrolls them. Idempotent on replay: an
    /// existing live subscription to the same plan is returned instead of creating a duplicate.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the given user's subscriptions as reflected by the billing system of record.</summary>
    Task<IReadOnlyList<SubscriptionInfo>> GetMySubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
