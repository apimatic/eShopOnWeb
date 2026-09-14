using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, delegated to an external billing system of record.
/// </summary>
/// <remarks>
/// This sits alongside — not inside — the one-time Catalog/Basket/Order flow. The billing
/// provider owns the customer and subscription records; eShopOnWeb stores no copy of them, so
/// every read reflects the provider's current truth and nothing has to be reconciled.
/// </remarks>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans currently on offer, cheapest first.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrols the subscriber in a plan, creating their billing customer record if needed.
    /// </summary>
    /// <remarks>
    /// Idempotent: repeated calls for the same subscriber and plan return the enrolment that
    /// already exists instead of creating another one.
    /// </remarks>
    Task<SubscribeToPlanResult> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriber's enrolments, newest first. Returns empty when they have no billing
    /// customer record yet; it never creates one.
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(BillingSubscriber subscriber, CancellationToken cancellationToken = default);
}
