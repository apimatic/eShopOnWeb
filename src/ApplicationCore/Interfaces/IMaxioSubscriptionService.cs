using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription billing facade backed by Maxio Advanced Billing (the billing
/// system of record). Customer/subscription creation is idempotent per user
/// and per plan, so repeated calls (e.g. double-clicks) never create duplicates.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available to shoppers.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given user, then enrolls them in
    /// the plan identified by <paramref name="productHandle"/>. Returns the
    /// resulting subscription (or the existing one when already subscribed).
    /// </summary>
    /// <exception cref="SubscriptionPlanNotFoundException">Thrown when no active plan matches the handle.</exception>
    Task<SubscriptionSummary> SubscribeAsync(string userId, string userName, string? email, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's subscriptions, with live state from the billing system.
    /// </summary>
    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsForUserAsync(string userId, string userName, CancellationToken cancellationToken = default);
}
