using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates recurring-subscription billing against Maxio for eShopOnWeb users. Maxio is
/// the system of record; this service maps an authenticated eShopOnWeb user to a Maxio customer
/// and enrolls them in plans idempotently.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Returns the plans available for subscription from the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given eShopOnWeb user (identified by their user name / login) to a plan.
    /// Ensures a Maxio customer exists for the user and enrolls them idempotently so a repeated
    /// or concurrent request never produces a duplicate customer or subscription.
    /// </summary>
    /// <param name="userName">The authenticated user's name, taken from their bearer token.</param>
    /// <param name="planHandle">The plan (product) handle to subscribe to; falls back to the default plan when null/empty.</param>
    Task<SubscribeResult> SubscribeAsync(string userName, string? planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the Maxio subscriptions for the given eShopOnWeb user.</summary>
    Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
