using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio.Dtos;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

/// <summary>
/// Thin HTTP client over the Maxio Advanced Billing REST API. Every endpoint, parameter and
/// schema used here is defined in the Maxio OpenAPI specification (maxio-spec/openapi.yaml),
/// which is the authoritative contract for the integration.
/// </summary>
public interface IMaxioBillingApi
{
    /// <summary>GET /product_families.json — spec operation <c>listProductFamilies</c>.</summary>
    Task<IReadOnlyList<MaxioProductFamilyEnvelope>> ListProductFamiliesAsync(CancellationToken ct);

    /// <summary>GET /product_families/{product_family_id}/products.json — spec operation <c>listProductsForProductFamily</c>.</summary>
    Task<IReadOnlyList<MaxioProductEnvelope>> ListProductsAsync(long productFamilyId, CancellationToken ct);

    /// <summary>GET /customers/lookup.json?reference=... — spec operation <c>readCustomerByReference</c>. Returns null when no customer matches.</summary>
    Task<MaxioCustomerEnvelope?> FindCustomerByReferenceAsync(string reference, CancellationToken ct);

    /// <summary>POST /customers.json — spec operation <c>createCustomer</c>.</summary>
    Task<MaxioCustomerEnvelope> CreateCustomerAsync(MaxioCreateCustomerEnvelope request, CancellationToken ct);

    /// <summary>GET /customers/{customer_id}/subscriptions.json — spec operation <c>listCustomerSubscriptions</c>.</summary>
    Task<IReadOnlyList<MaxioSubscriptionEnvelope>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken ct);

    /// <summary>POST /subscriptions.json — spec operation <c>createSubscription</c>.</summary>
    Task<MaxioSubscriptionEnvelope> CreateSubscriptionAsync(MaxioCreateSubscriptionEnvelope request, CancellationToken ct);
}
