using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing (the system of record).
/// Implementations translate all provider failures into
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.BillingProviderException"/> so callers
/// have a single failure type to handle.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans available in the configured product family.</summary>
    Task<SubscriptionPlanList> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the shopper (idempotent) and enrolls them in the
    /// requested plan. A double-click returns the existing subscription rather than creating a second.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns the shopper's subscriptions (empty when the shopper has no Maxio customer yet).</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}
