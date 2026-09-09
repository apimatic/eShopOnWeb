using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing as the system of record.
/// Implementations own all provider communication; callers see only SDK-free DTOs and
/// <see cref="Exceptions.SubscriptionBillingException"/> on failure.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscribable plans in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given shopper to a plan. Idempotent: ensures a single Maxio customer exists for
    /// the user and a single subscription exists for the (user, plan) pair, so a double-click never
    /// creates duplicates. Returns the resulting subscription with its plan, price, state and next
    /// billing date.
    /// </summary>
    Task<CustomerSubscriptionDto> SubscribeAsync(BillingAppUser user, SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's subscriptions. Empty when the user has no Maxio customer yet.</summary>
    Task<IReadOnlyList<CustomerSubscriptionDto>> GetMySubscriptionsAsync(BillingAppUser user, CancellationToken cancellationToken = default);
}
