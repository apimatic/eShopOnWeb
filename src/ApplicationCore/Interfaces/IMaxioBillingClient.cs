using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port to the external subscription billing system (Maxio Advanced Billing),
/// which is the system of record for plans, customers and subscriptions.
/// </summary>
public interface IMaxioBillingClient
{
    /// <summary>
    /// Lists the subscribable (non-archived) plans of the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a billing customer by the external reference, or null when none exists.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a billing customer with the given external reference.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string? lastName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription for a billing customer to the plan with the given handle.
    /// </summary>
    Task<MaxioSubscriptionInfo> CreateSubscriptionAsync(int billingCustomerId, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions of a billing customer (all states, newest first).
    /// </summary>
    Task<IReadOnlyList<MaxioSubscriptionInfo>> ListCustomerSubscriptionsAsync(int billingCustomerId,
        CancellationToken cancellationToken = default);
}
