using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin client for the Maxio Advanced Billing (Chargify) API. Hand-written against
/// the OpenAPI specification in maxio-spec/openapi.yaml, which is the authoritative
/// contract for every operation, path, parameter, schema and error model used here.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// Lists all products in the site, paging through the full result set.
    /// Spec: GET /products.json (listProducts).
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListAllProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a customer by the application-provided reference. Returns null when no
    /// customer has that reference (HTTP 404).
    /// Spec: GET /customers/lookup.json (readCustomerByReference).
    /// </summary>
    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer. Maxio enforces that only one customer may exist for a
    /// given reference value.
    /// Spec: POST /customers.json (createCustomer).
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a subscription by the application-provided reference. Returns null when
    /// no subscription has that reference (HTTP 404).
    /// Spec: GET /subscriptions/lookup.json (findSubscription).
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription for a customer and product. Uses the spec's
    /// remittance collection method so a subscription can be created without
    /// capturing a payment method (the demo plans require none).
    /// Spec: POST /subscriptions.json (createSubscription).
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions that belong to a customer.
    /// Spec: GET /customers/{customer_id}/subscriptions.json (listCustomerSubscriptions).
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
