using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Application-level subscription operations backed by Maxio Advanced Billing.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans (Maxio products) available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given application user (idempotent),
    /// then subscribes them to the given plan. When <paramref name="planHandle"/> is
    /// null, the first plan in the configured product family is used. Repeated calls
    /// for the same user and plan are idempotent and never create a second subscription.
    /// </summary>
    Task<MaxioSubscribeResult> SubscribeAsync(string ownerId, string userName, string email, string? planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the current Maxio subscriptions of the given application user.
    /// Returns an empty list when the user has no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForUserAsync(string ownerId, CancellationToken cancellationToken = default);
}