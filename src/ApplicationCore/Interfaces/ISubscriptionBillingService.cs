using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record (Maxio
/// Advanced Billing). This is an additive capability alongside the app's one-time commerce
/// flow — it never touches the Basket/Order aggregates.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans available for enrollment (the products in the configured
    /// product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a user in a plan. Idempotent: ensures a single billing customer exists for the
    /// user and returns the existing live subscription for the plan instead of creating a
    /// duplicate when one is already present.
    /// </summary>
    /// <exception cref="Exceptions.SubscriptionPlanNotFoundException">The requested plan handle is not offered.</exception>
    /// <exception cref="Exceptions.BillingServiceException">The billing system rejected or could not fulfil the request.</exception>
    Task<CustomerSubscription> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscriptions currently on record for the given user. Empty when the user
    /// has never been enrolled (no billing customer exists yet).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(BillingCustomerIdentity customer, CancellationToken cancellationToken = default);
}
