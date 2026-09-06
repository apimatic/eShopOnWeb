using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin transport over the Maxio Advanced Billing REST API. Speaks Maxio's wire shapes only; all
/// policy (idempotency, plan selection, mapping) lives in
/// <see cref="MaxioSubscriptionService"/>.
/// </summary>
internal interface IMaxioApiClient
{
    /// <summary>Reads site metadata, notably the primary currency. <c>GET /site.json</c>.</summary>
    Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every non-archived product in a product family, following pagination.
    /// <c>GET /product_families/handle:{handle}/products.json</c>.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a customer by the reference this application assigned, or <c>null</c> when there is none.
    /// <c>GET /customers/lookup.json</c>.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates a customer. <c>POST /customers.json</c>.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription belonging to a customer, in any state.
    /// <c>GET /customers/{id}/subscriptions.json</c>.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a subscription. <c>POST /subscriptions.json</c>.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateMaxioSubscriptionRequest request,
        CancellationToken cancellationToken = default);
}
