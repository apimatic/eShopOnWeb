using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin client over the subset of the Maxio Advanced Billing API (maxio-spec/openapi.yaml)
/// needed to support subscription billing: customers, products, and subscriptions.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// GET /customers/lookup.json?reference={reference}. Returns null when no customer
    /// with that reference exists (Maxio responds 404).
    /// </summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// POST /customers.json. If a concurrent request already created a customer with the
    /// same reference, Maxio's uniqueness constraint on reference rejects this one with a
    /// 422; in that case the existing customer is looked up and returned instead.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /product_families/handle:{productFamilyHandle}/products.json - the plans available to subscribe to.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /customers/{customerId}/subscriptions.json
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// POST /subscriptions.json
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest request, CancellationToken cancellationToken = default);
}
