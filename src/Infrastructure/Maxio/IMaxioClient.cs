using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin typed client over the Maxio Advanced Billing HTTP API. Each method maps to a single operation in
/// the OpenAPI spec (maxio-spec/). It deals only in Maxio contract types; higher-level orchestration and
/// mapping to eShopOnWeb models live in <see cref="MaxioBillingService"/>.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// GET /product_families/{product_family_id}/products.json — lists products in a family.
    /// <paramref name="productFamilyIdOrHandle"/> may be a numeric id or a handle prefixed with "handle:".
    /// </summary>
    Task<IReadOnlyList<ProductEnvelope>> ListProductsForFamilyAsync(string productFamilyIdOrHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /customers/lookup.json?reference=... — returns the customer with the given reference, or null if none exists.
    /// </summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json — creates a customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json — creates a subscription.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json — lists a customer's subscriptions.</summary>
    Task<IReadOnlyList<SubscriptionEnvelope>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
