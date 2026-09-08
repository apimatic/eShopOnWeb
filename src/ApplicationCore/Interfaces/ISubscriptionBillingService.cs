using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing as the billing system of record.
/// All operations are idempotent with respect to the shop user: a user maps to exactly one
/// Maxio customer (via the customer reference), and re-subscribing to an already-held live plan
/// returns the existing subscription instead of creating a duplicate.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans available for subscription in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user (idempotently, keyed by <see cref="SubscribeCommand.UserId"/>),
    /// then enrolls them in the requested plan. If the user already holds a live subscription for the plan,
    /// the existing subscription is returned instead of creating a duplicate.
    /// </summary>
    /// <exception cref="Exceptions.SubscriptionPlanNotFoundException">The plan handle is not a subscribable plan.</exception>
    Task<SubscriptionDetails> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all of the user's Maxio subscriptions. Empty if the user has no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListForUserAsync(string userId, CancellationToken cancellationToken = default);
}
