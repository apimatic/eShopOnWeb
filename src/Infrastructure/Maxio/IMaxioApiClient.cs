using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed client for the Maxio Advanced Billing API. Built strictly against the
/// OpenAPI specification in maxio-spec/openapi.yaml (endpoints, parameters,
/// envelopes and error models). Authentication is HTTP Basic with the API key
/// as username and "x" as password, per the spec's security scheme.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// GET /product_families/{product_family_id}/products.json — lists the products
    /// belonging to a product family. The path parameter accepts the family's
    /// handle prefixed with "handle:" per the spec.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /customers/lookup.json?reference=... — returns the customer with the given
    /// reference, or null when the API responds with 404 Not Found.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// POST /customers.json — creates a customer. The spec guarantees only one
    /// customer per reference value.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /subscriptions/lookup.json?reference=... — returns the subscription with
    /// the given reference, or null when the API responds with 404 Not Found.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// POST /subscriptions.json — creates a subscription for an existing customer.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /customers/{customer_id}/subscriptions.json — lists all subscriptions
    /// belonging to a customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}