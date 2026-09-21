using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by the billing provider (Maxio Advanced Billing). Implementations
/// live in Infrastructure and keep all provider/SDK details behind this provider-agnostic contract.
/// All methods throw <see cref="SubscriptionBillingException"/> on failure.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans available to subscribe to (the products in the configured product family).</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the eShop user (idempotent by <see cref="SubscribeRequest.UserReference"/>)
    /// and enrolls them in the requested plan. Idempotent against double-submit: an existing live subscription to
    /// the same plan is returned rather than creating a second one.
    /// </summary>
    Task<SubscribeOutcome> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions belonging to the eShop user identified by <paramref name="userReference"/>.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}
