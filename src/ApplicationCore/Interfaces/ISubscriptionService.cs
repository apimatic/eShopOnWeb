using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provides recurring-subscription billing capabilities with Maxio Advanced Billing
/// as the billing system of record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available for shoppers.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given user to a plan. Ensures a billing customer exists for the
    /// user (idempotently) and never creates a duplicate live subscription for the same
    /// plan; if one already exists it is returned instead. The returned flag indicates
    /// whether a new subscription was created (true) or an existing one was returned (false).
    /// </summary>
    Task<(SubscriptionDetails Subscription, bool Created)> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's subscriptions in the billing system.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default);
}
