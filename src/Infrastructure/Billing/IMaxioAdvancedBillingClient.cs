using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing REST API.
/// Paths and shapes are those documented by Maxio (Chargify-compatible API).
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    Task<MaxioCustomer?> GetCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(
        CreateCustomer customer,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateSubscription subscription,
        CancellationToken cancellationToken = default);
}
