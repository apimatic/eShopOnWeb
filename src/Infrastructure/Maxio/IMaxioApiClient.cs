using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Transport for the Maxio Advanced Billing API. Every member maps one-to-one onto an operation of
/// the OpenAPI specification in <c>maxio-spec/</c>; no behaviour beyond request shaping, response
/// deserialisation and error translation lives here.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// <c>listProductsForProductFamily</c> - <c>GET /product_families/{product_family_id}/products.json</c>.
    /// The path segment accepts either a numeric id or a handle prefixed with <c>handle:</c>.
    /// Follows pagination until the family is exhausted.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyIdOrHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>readCustomerByReference</c> - <c>GET /customers/lookup.json?reference=...</c>.
    /// Returns <c>null</c> when no customer carries that reference (the operation answers 404).
    /// </summary>
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>createCustomer</c> - <c>POST /customers.json</c>.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listCustomerSubscriptions</c> - <c>GET /customers/{customer_id}/subscriptions.json</c>.
    /// Returns an empty list when the customer is unknown to Maxio.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>findSubscription</c> - <c>GET /subscriptions/lookup.json?reference=...</c>.
    /// Returns <c>null</c> when no subscription carries that reference.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>createSubscription</c> - <c>POST /subscriptions.json</c>.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default);
}
