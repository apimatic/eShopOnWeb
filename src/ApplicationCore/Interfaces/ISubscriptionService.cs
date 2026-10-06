using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing (the system of record).
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given reference (idempotent) and subscribes them
    /// to the given plan. If the customer already has a live subscription to that plan, the
    /// existing subscription is returned instead of creating a duplicate.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the Maxio customer identified by <paramref name="customerReference"/>.
    /// Returns an empty list when no customer exists yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
