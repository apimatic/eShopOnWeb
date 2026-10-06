using Maxio.Models;

namespace Maxio;

/// <summary>
/// Client for the Maxio Advanced Billing API. Every interaction is built against the Maxio OpenAPI
/// specification (maxio-spec/openapi.yaml), which is the authoritative contract.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// Reads a customer by its unique reference value (GET /customers/lookup.json).
    /// Returns null when no customer matches the reference.
    /// </summary>
    Task<Customer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer (POST /customers.json).
    /// </summary>
    Task<Customer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the products belonging to a product family (GET /product_families/{product_family_id}/products.json).
    /// The family may be identified by its numeric id or by its handle prefixed with "handle:".
    /// </summary>
    Task<IReadOnlyList<Product>> ListProductsForProductFamilyAsync(string productFamilyIdOrHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription (POST /subscriptions.json).
    /// </summary>
    Task<Subscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions that belong to a customer (GET /customers/{customer_id}/subscriptions.json).
    /// </summary>
    Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
