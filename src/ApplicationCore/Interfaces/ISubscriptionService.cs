using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring subscription billing against Maxio Advanced Billing (the system of record).
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available for enrollment.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanSummary>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing-system customer exists for the user (idempotent, keyed by user id)
    /// and enrolls them in the plan identified by <paramref name="productHandle"/>.
    /// If the user already holds a live subscription to that plan, the existing subscription
    /// is returned instead of creating a duplicate.
    /// </summary>
    Task<SubscriptionSummary> SubscribeAsync(SubscriptionUserContext user, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions the user currently holds in the billing system.
    /// </summary>
    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsForUserAsync(SubscriptionUserContext user, CancellationToken cancellationToken = default);
}