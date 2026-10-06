using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates recurring-subscription billing for eShopOnWeb users with
/// Maxio Advanced Billing as the billing system of record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans available for subscription.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes a user to a plan. Idempotent: enrolling an already-enrolled
    /// user in the same plan returns the existing subscription without
    /// creating a duplicate customer or subscription in Maxio.
    /// </summary>
    Task<Result<Subscription>> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions recorded locally for a user.
    /// </summary>
    Task<IReadOnlyList<Subscription>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);
}