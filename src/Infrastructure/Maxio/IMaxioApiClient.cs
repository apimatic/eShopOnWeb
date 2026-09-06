using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The subset of the Maxio Advanced Billing API this integration uses. Every member corresponds
/// one-to-one to an operation in the Maxio OpenAPI specification (<c>maxio-spec/openapi.yaml</c>),
/// named after that operation's <c>operationId</c>.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// <c>listProductsForProductFamily</c> - <c>GET /product_families/{product_family_id}/products.json</c>.
    /// </summary>
    /// <param name="productFamilyIdOrHandle">
    /// Either the family's numeric id or its handle prefixed with <c>handle:</c>.
    /// </param>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        int page,
        int perPage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>readCustomerByReference</c> - <c>GET /customers/lookup.json</c>.
    /// Returns <see langword="null"/> when no customer carries that reference.
    /// </summary>
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>createCustomer</c> - <c>POST /customers.json</c>.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listCustomerSubscriptions</c> - <c>GET /customers/{customer_id}/subscriptions.json</c>.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>createSubscription</c> - <c>POST /subscriptions.json</c>.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>findSubscription</c> - <c>GET /subscriptions/lookup.json</c>.
    /// Returns <see langword="null"/> when no subscription carries that reference.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);
}
