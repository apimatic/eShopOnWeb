using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing. Additive and parallel
/// to the existing Catalog/Basket/Order one-time-purchase flow.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>The plans available for subscription in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the caller (idempotent) and enrolls them in the
    /// requested plan. If the caller already has a live (non-terminal) subscription to that
    /// plan, that existing subscription is returned instead of creating a duplicate.
    /// </summary>
    Task<SubscriptionEnrollment> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// The caller's subscriptions. Returns an empty list when no Maxio customer has been
    /// created for them yet (i.e. they have never subscribed).
    /// </summary>
    Task<IReadOnlyList<SubscriptionEnrollment>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default);
}
