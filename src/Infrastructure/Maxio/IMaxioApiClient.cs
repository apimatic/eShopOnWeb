using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed access to the Maxio Advanced Billing operations this integration uses. Every member maps
/// one-to-one onto an operation declared in the Maxio OpenAPI specification (the operation id is
/// named on each member).
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>Spec operation <c>readSite</c> — <c>GET /site.json</c>.</summary>
    Task<Site> ReadSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>Spec operation <c>listProductFamilies</c> — <c>GET /product_families.json</c>.</summary>
    Task<IReadOnlyList<ProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Spec operation <c>listProductsForProductFamily</c> —
    /// <c>GET /product_families/{product_family_id}/products.json</c>. Follows pagination to completion.
    /// </summary>
    Task<IReadOnlyList<Product>> ListProductsForProductFamilyAsync(
        int productFamilyId,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Spec operation <c>readCustomerByReference</c> — <c>GET /customers/lookup.json?reference=...</c>.
    /// Returns <c>null</c> when no customer carries that reference.
    /// </summary>
    Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Spec operation <c>createCustomer</c> — <c>POST /customers.json</c>.</summary>
    Task<Customer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>Spec operation <c>createSubscription</c> — <c>POST /subscriptions.json</c>.</summary>
    Task<Subscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Spec operation <c>listCustomerSubscriptions</c> —
    /// <c>GET /customers/{customer_id}/subscriptions.json</c>.
    /// </summary>
    Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
