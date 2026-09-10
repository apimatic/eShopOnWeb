using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, verified wrapper over the Maxio Advanced Billing REST API. Each method maps
/// to one confirmed endpoint. Unsuccessful responses throw <see cref="MaxioApiException"/>
/// (except customer lookup, which returns null on 404).
/// </summary>
internal interface IMaxioClient
{
    /// <summary>GET /product_families.json</summary>
    Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken = default);

    /// <summary>GET /product_families/{productFamilyId}/products.json (numeric id required; handle is not accepted here).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference={reference} — returns null on 404 (no such customer).</summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody customer, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody subscription, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customerId}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
