using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A thin, typed HTTP client over the subset of the Maxio Advanced Billing API used by eShopOnWeb.
/// Every operation maps to an endpoint defined in the authoritative OpenAPI spec (maxio-spec/openapi.yaml).
/// </summary>
internal interface IMaxioApiClient
{
    /// <summary>GET /product_families/handle:{familyHandle}/products.json — list the plans in a family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken);

    /// <summary>GET /customers/lookup.json?reference=... — returns null when no customer matches (HTTP 404).</summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /customers.json — create a customer. Throws MaxioValidationException on HTTP 422.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody customer, CancellationToken cancellationToken);

    /// <summary>POST /subscriptions.json — create a subscription. Throws MaxioValidationException on HTTP 422.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody subscription, CancellationToken cancellationToken);

    /// <summary>GET /customers/{customer_id}/subscriptions.json — list a customer's subscriptions.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}
