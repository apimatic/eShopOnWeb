using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription-billing capability exposed to the API layer.
/// Maxio Advanced Billing is the billing system of record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the subscription plans (products of the configured product family).</summary>
    Task<Result<IReadOnlyList<MaxioPlan>>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the given user in a plan. Idempotent: returns the existing live
    /// subscription instead of creating a duplicate.
    /// </summary>
    Task<Result<SubscriptionEnrollment>> SubscribeAsync(string userName, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the user's Maxio subscriptions (empty when never enrolled).</summary>
    Task<Result<IReadOnlyList<MaxioSubscription>>> GetSubscriptionsForUserAsync(string userName,
        CancellationToken cancellationToken = default);
}