using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing against the external billing system of record
/// (Maxio Advanced Billing). The hero flow: browse plans, subscribe, list own
/// subscriptions.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans that can be subscribed to, from the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a billing customer exists for the user and subscribes them to the plan.
    /// Idempotent: calling it twice with the same user and plan returns the existing
    /// subscription instead of creating a second one.
    /// </summary>
    /// <exception cref="Exceptions.MaxioBillingException">When the billing provider rejects or cannot confirm the operation.</exception>
    Task<SubscriptionSummary> SubscribeAsync(SubscriberProfile profile, string planHandle, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the user's subscriptions held by the billing system; empty when the
    /// user has never been provisioned there.
    /// </summary>
    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsForUserAsync(SubscriberProfile profile, CancellationToken cancellationToken);
}