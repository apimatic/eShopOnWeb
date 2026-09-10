using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Thin, typed transport over the subset of the Maxio Advanced Billing REST API this integration needs.
/// One method per confirmed endpoint; no business logic. Internal to the infrastructure layer.
/// </summary>
internal interface IMaxioApiClient
{
    /// <summary>GET /product_families/handle:{familyHandle}/products.json</summary>
    Task<IReadOnlyList<ProductWire>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken);

    /// <summary>GET /customers/lookup.json?reference={reference} — returns null when no customer matches (404).</summary>
    Task<CustomerWire?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /customers.json</summary>
    Task<CustomerWire> CreateCustomerAsync(CreateCustomerWire customer, CancellationToken cancellationToken);

    /// <summary>POST /subscriptions.json</summary>
    Task<SubscriptionWire> CreateSubscriptionAsync(CreateSubscriptionWire subscription, CancellationToken cancellationToken);

    /// <summary>GET /customers/{customerId}/subscriptions.json</summary>
    Task<IReadOnlyList<SubscriptionWire>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
}
