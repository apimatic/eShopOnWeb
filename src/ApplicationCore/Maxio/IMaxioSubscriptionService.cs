using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Application-facing subscription-billing capability, backed by Maxio Advanced Billing
/// (the system of record for customers and subscriptions).
/// </summary>
public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given eShopOnWeb user and enrolls them in the given plan.
    /// Idempotent: a repeated call for a user who already has a live subscription to that plan returns
    /// the existing subscription rather than creating a new one.
    /// </summary>
    Task<SubscriptionSummary> SubscribeAsync(string userId, string userEmail, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the given user's subscriptions. Empty if the user has never subscribed (no Maxio customer yet).
    /// </summary>
    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);
}
