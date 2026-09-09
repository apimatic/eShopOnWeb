using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing as the billing
/// system of record. Customer and subscription identity is kept idempotent through
/// Maxio reference values, so no local persistence is required.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans available in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user (idempotent by reference) and
    /// subscribes them to the requested plan (idempotent while the subscription is live).
    /// </summary>
    Task<SubscriptionDetails> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's Maxio subscriptions. Returns an empty list when the user has
    /// never been enrolled.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels one of the user's subscriptions. Throws <see cref="System.UnauthorizedAccessException"/>
    /// when the subscription does not belong to the user.
    /// </summary>
    Task<SubscriptionDetails> CancelAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default);
}
