using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing operations backed by the billing system of record (Maxio Advanced Billing).
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans currently offered (non-archived products in the configured product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListSubscriptionPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a customer in a plan. Ensures the billing customer exists first (created at most once per
    /// customer reference) and returns the customer's existing live subscription when one is already present,
    /// so repeating the call never creates duplicate customers or subscriptions.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription belonging to the customer. Returns an empty list when the customer
    /// has never been enrolled in the billing system.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default);
}
