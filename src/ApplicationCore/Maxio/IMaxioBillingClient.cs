using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Thin abstraction over the Maxio Advanced Billing API, built to the OpenAPI
/// specification in maxio-spec/ (operations: listProductsForProductFamily,
/// readCustomerByReference, createCustomer, listCustomerSubscriptions, createSubscription).
/// </summary>
public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandleOrId, CancellationToken cancellationToken = default);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioNewCustomerRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioNewSubscriptionRequest request, CancellationToken cancellationToken = default);

    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);
}
