using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin, spec-aligned HTTP client for the Maxio Advanced Billing API.
/// Every operation corresponds one-to-one with an endpoint defined in maxio-spec/openapi.yaml.
/// </summary>
public interface IMaxioClient
{
    /// <summary>GET /product_families/{product_family_id}/products.json (family id may be a handle:… value).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken);

    /// <summary>GET /customers/lookup.json?reference={reference}. Returns null when no customer matches.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /customers.json.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken);

    /// <summary>GET /customers/{customer_id}/subscriptions.json.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);

    /// <summary>POST /subscriptions.json.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionInput subscription, CancellationToken cancellationToken);
}
