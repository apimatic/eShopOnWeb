using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing operations, backed by Maxio Advanced Billing as the
/// system of record. Implementations are expected to be idempotent on the customer's
/// stable reference so retries and double-clicks never create duplicate customers or
/// duplicate active subscriptions.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the plans a shopper can subscribe to (the products under the configured
    /// Maxio product family), cheapest first.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="customer"/> (keyed on its
    /// reference) and enrolls them in the plan identified by <paramref name="planHandle"/>.
    /// If the customer already has a live subscription to that plan it is returned as-is.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(BillingCustomer customer, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every subscription for the customer identified by
    /// <paramref name="customerReference"/>, newest first. Empty when the customer has no
    /// Maxio record yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
