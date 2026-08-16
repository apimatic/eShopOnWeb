using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin typed client over the Maxio Advanced Billing HTTP API. Each method maps to exactly one
/// operation in the OpenAPI spec (maxio-spec/openapi.yaml) and returns the vendor wire models.
/// Orchestration and mapping to domain models live in <see cref="MaxioBillingService"/>.
/// </summary>
internal interface IMaxioApiClient
{
    /// <summary>GET /product_families/{product_family_id}/products.json (id or <c>handle:</c> prefix).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdentifier, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference=... Returns null when no customer matches.</summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(
        CreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateSubscriptionRequest request, CancellationToken cancellationToken = default);
}
