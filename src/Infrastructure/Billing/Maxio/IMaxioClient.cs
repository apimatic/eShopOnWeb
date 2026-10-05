using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Typed client for the Maxio Advanced Billing API. Every operation is built directly
/// against the OpenAPI specification in maxio-spec/openapi.yaml (the authoritative contract):
/// paths, query parameters, auth scheme (Basic, API key as username / "x" as password)
/// and response schemas.
/// </summary>
public interface IMaxioClient
{
    /// <summary>Read Customer by Reference — GET /customers/lookup.json?reference={reference}. Returns null on 404.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Create Customer — POST /customers.json.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, CancellationToken cancellationToken = default);

    /// <summary>List Products for Product Family — GET /product_families/{product_family_id}/products.json.
    /// The path parameter accepts the family id or its handle prefixed with "handle:" (per the spec).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyIdOrHandle, CancellationToken cancellationToken = default);

    /// <summary>Create Subscription — POST /subscriptions.json (returns 201 with the subscription wrapper).</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate subscription, CancellationToken cancellationToken = default);

    /// <summary>List Customer Subscriptions — GET /customers/{customer_id}/subscriptions.json.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Read Subscription — GET /subscriptions/{subscription_id}.json. Returns null on 404.</summary>
    Task<MaxioSubscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}