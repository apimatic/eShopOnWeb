using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The billing provider, seen from the application. Every failure surfaces as
/// <see cref="BillingProviderException"/>; caller cancellation propagates as <see cref="System.OperationCanceledException"/>.
/// </summary>
public interface ISubscriptionBillingGateway
{
    /// <summary>The product family whose products are the plans this application offers.</summary>
    string ProductFamilyHandle { get; }

    Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the provider id of the customer with this reference, or null when there is none.</summary>
    Task<int?> FindCustomerIdAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>Finds the customer by reference, creating it when absent. Safe to call repeatedly.</summary>
    Task<int> EnsureCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription carrying <paramref name="subscriptionReference"/>. Never reports a failure for a write
    /// that may have landed: such outcomes are settled by reference, or returned as unknown. A thrown
    /// <see cref="BillingProviderException"/> therefore always means the provider created nothing.
    /// </summary>
    Task<SubscriptionCreationResult> CreateSubscriptionAsync(int customerId, string planHandle,
        string subscriptionReference, CancellationToken cancellationToken = default);

    /// <summary>Returns the subscription with this reference, or null when the provider has none.</summary>
    Task<BillingSubscription?> FindSubscriptionAsync(string subscriptionReference, CancellationToken cancellationToken = default);

    /// <summary>Returns the subscription with this provider id, or null when the provider has none.</summary>
    Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
