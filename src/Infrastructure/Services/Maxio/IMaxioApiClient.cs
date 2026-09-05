using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Thin wrapper over the Maxio Advanced Billing REST API. Deliberately narrow - only the calls
/// this integration needs - and internal, since callers should depend on
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Interfaces.IMaxioBillingService"/> instead.
/// </summary>
internal interface IMaxioApiClient
{
    /// <summary>GET /product_families/handle:{familyHandle}/products.json</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken);

    /// <summary>GET /products/handle/{handle}.json; null on 404.</summary>
    Task<MaxioProduct?> FindProductByHandleAsync(string productHandle, CancellationToken cancellationToken);

    /// <summary>GET /customers/lookup.json?reference={reference}; null on 404.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerAttributes attributes, CancellationToken cancellationToken);

    /// <summary>GET /customers/{customerId}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionAttributes attributes, CancellationToken cancellationToken);
}
