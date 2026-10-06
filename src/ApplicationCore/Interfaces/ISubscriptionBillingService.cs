using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A subscription plan offered by the billing system of record.
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int Interval,
    string IntervalUnit);

/// <summary>
/// The state of a user's subscription, as reported by the billing system of record.
/// </summary>
public record SubscriptionDetails(
    int SubscriptionId,
    string PlanHandle,
    string PlanName,
    decimal Price,
    string State,
    DateTime? NextBillingDate,
    DateTime? CreatedAt);

/// <summary>
/// Contract for recurring-subscription billing against the external billing system of record.
/// Implementations must be idempotent: repeated calls for the same user and plan must never
/// create duplicate customers or subscriptions.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans available for enrollment.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing-system customer exists for the user, then enrolls them in the plan
    /// identified by <paramref name="planHandle"/>. Returns the resulting subscription state.
    /// </summary>
    Task<SubscriptionDetails> SubscribeAsync(string userEmail, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions held by the user.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsForUserAsync(string userEmail, CancellationToken cancellationToken = default);
}