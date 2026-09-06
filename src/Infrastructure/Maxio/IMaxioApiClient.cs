using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The subset of the Maxio Advanced Billing API this integration uses. Every member maps one-to-one
/// onto an operation declared in maxio-spec/openapi.yaml; the operationId is named on each method.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// <c>listProductsForProductFamily</c> - GET /product_families/{product_family_id}/products.json.
    /// The family is addressed by handle, using the <c>handle:</c> prefix the path parameter accepts.
    /// Pages are followed until the family is exhausted.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>readCustomerByReference</c> - GET /customers/lookup.json?reference=...
    /// Returns null when no customer carries that reference (Maxio answers 404).
    /// </summary>
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>createCustomer</c> - POST /customers.json.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listCustomerSubscriptions</c> - GET /customers/{customer_id}/subscriptions.json.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>findSubscription</c> - GET /subscriptions/lookup.json?reference=...
    /// Returns null when no subscription carries that reference (Maxio answers 404).
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>createSubscription</c> - POST /subscriptions.json.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default);
}
