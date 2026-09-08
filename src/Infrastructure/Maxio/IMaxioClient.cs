using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level operations against the Maxio Advanced Billing JSON API.
/// Implementations must be resilient (timeouts, HTTP status mapping) and must
/// treat 404 on lookups as "not found" (null) rather than an error.
/// </summary>
public interface IMaxioClient
{
    /// <summary>GET /product_families/{handle}.json — resolves a product family by its stable handle.</summary>
    Task<MaxioProductFamily?> FindProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken);

    /// <summary>GET /product_families/{id}/products.json — lists products in a family (includes archived ones; caller filters).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(int productFamilyId, CancellationToken cancellationToken);

    /// <summary>GET /products/handle/{handle}.json — reads a single product by handle.</summary>
    Task<MaxioProduct?> FindProductByHandleAsync(string handle, CancellationToken cancellationToken);

    /// <summary>GET /customers/lookup.json?reference={reference} — idempotent customer lookup by reference. Null when absent.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /customers.json — creates a customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken);

    /// <summary>GET /customers/{id}/subscriptions.json — lists all subscriptions of a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>POST /subscriptions.json — creates a subscription for a customer. NOTE: Maxio allows duplicates; the application layer enforces idempotency.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken);
}
