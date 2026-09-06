using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, one-method-per-operation client over the Maxio Advanced Billing API. Each member names the
/// <c>operationId</c> it implements in maxio-spec/openapi.yaml. It speaks wire models only; mapping
/// onto the eShopOnWeb domain happens in <see cref="MaxioBillingGateway"/>.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary><c>readSite</c> — <c>GET /site.json</c>.</summary>
    Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listProductsForProductFamily</c> — <c>GET /product_families/{product_family_id}/products.json</c>.
    /// The family is addressed by handle, which the specification permits via the <c>handle:</c> prefix.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>readCustomerByReference</c> — <c>GET /customers/lookup.json</c>.
    /// Returns <c>null</c> when Maxio answers 404.
    /// </summary>
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>createCustomer</c> — <c>POST /customers.json</c>.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listCustomerSubscriptions</c> — <c>GET /customers/{customer_id}/subscriptions.json</c>.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>findSubscription</c> — <c>GET /subscriptions/lookup.json</c>.
    /// Returns <c>null</c> when Maxio answers 404.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>createSubscription</c> — <c>POST /subscriptions.json</c>.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default);
}
