using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed client for the subset of the Maxio Advanced Billing API (maxio-spec/openapi.yaml)
/// used by the subscription billing capability.
/// </summary>
public interface IMaxioClient
{
    /// <summary>GET /product_families.json (listProductFamilies)</summary>
    Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken = default);

    /// <summary>GET /product_families/{product_family_id}/products.json (listProductsForProductFamily)</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference=... (readCustomerByReference). Returns null when no customer matches (404).</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json (createCustomer)</summary>
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json (listCustomerSubscriptions)</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json (createSubscription)</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string? reference = null, CancellationToken cancellationToken = default);
}
