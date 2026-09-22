using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing provider (the system of record).
/// Implementations translate all provider failures into <see cref="Exceptions.BillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans currently offered by the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes <paramref name="customer"/> to the plan identified by <paramref name="planHandle"/>.
    /// Idempotent: ensures a single provider customer for the user (by reference) and a single
    /// subscription for that (user, plan) — a repeated call returns the existing subscription rather
    /// than creating a second one.
    /// </summary>
    /// <exception cref="Exceptions.UnknownPlanException">The plan handle is not offered by the family.</exception>
    /// <exception cref="Exceptions.BillingException">The provider rejected or could not complete the request.</exception>
    Task<SubscriptionDetails> SubscribeAsync(BillingCustomer customer, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to <paramref name="customer"/>. Returns an empty list when the
    /// user has no provider customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(BillingCustomer customer,
        CancellationToken cancellationToken = default);
}
