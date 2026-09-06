using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Http;

/// <summary>
/// The slice of the Maxio Advanced Billing REST API this integration uses.
/// </summary>
/// <remarks>
/// One method per Maxio endpoint, with no business rules: this is the transport seam. Every
/// signature corresponds to a documented endpoint, listed on the implementation.
/// </remarks>
internal interface IMaxioApiClient
{
    /// <summary>Reads the site, chiefly for its default currency. <c>GET /site.json</c></summary>
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the products in a product family, addressed by handle.
    /// <c>GET /product_families/handle:{handle}/products.json</c>
    /// </summary>
    /// <returns>The family's products, or <c>null</c> when no such family exists.</returns>
    Task<IReadOnlyList<MaxioProduct>?> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a customer by the reference this application assigned.
    /// <c>GET /customers/lookup.json?reference=...</c>
    /// </summary>
    /// <returns>The customer, or <c>null</c> when the reference is unknown.</returns>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates a customer. <c>POST /customers.json</c></summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription belonging to a customer.
    /// <c>GET /customers/{customer_id}/subscriptions.json</c>
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>Creates a subscription. <c>POST /subscriptions.json</c></summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a subscription by the reference this application assigned.
    /// <c>GET /subscriptions/lookup.json?reference=...</c>
    /// </summary>
    /// <returns>The subscription, or <c>null</c> when the reference is unknown.</returns>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);
}
