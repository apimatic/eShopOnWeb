using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing as the system of record.
/// This is an additive capability alongside the existing one-time commerce flow. Implementations
/// own all interaction with the billing SDK; callers work only with domain types.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans a shopper can subscribe to (the products in the configured product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently enrolls a shopper in a plan: ensures a billing customer exists for
    /// <see cref="SubscribeRequest.CustomerReference"/>, then creates the subscription if the
    /// customer is not already actively subscribed to that plan. A double-submit resolves to the
    /// existing customer/subscription rather than creating a duplicate.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the customer identified by <paramref name="customerReference"/>.
    /// Returns an empty list when the customer does not yet exist in the billing system.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default);
}
