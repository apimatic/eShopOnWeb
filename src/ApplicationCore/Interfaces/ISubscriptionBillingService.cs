using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing as the system of record.
/// This is an additive, parallel capability to the existing one-time commerce flow.
/// Implementations translate all provider failures into
/// <see cref="Exceptions.SubscriptionBillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans available under the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently enrolls the user in a plan: ensures a Maxio customer exists for the
    /// user (by <c>reference</c>) and creates the subscription only if a live one to the
    /// same plan does not already exist. A double-click never creates two customers or
    /// two subscriptions.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's subscriptions. Returns an empty list when the user has no Maxio
    /// customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default);
}
