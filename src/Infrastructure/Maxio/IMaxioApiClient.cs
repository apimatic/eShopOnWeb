using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The slice of the Maxio Billing API that eShopOnWeb speaks to. Deliberately a thin transport
/// contract — one method per documented endpoint, no orchestration — so the subscribe workflow can
/// be unit tested without HTTP.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /site.json — site metadata, notably the currency prices are quoted in.</summary>
    Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /product_families/handle:{handle}/products.json — the plans published on a product family,
    /// addressed by its stable handle rather than a numeric id.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json — returns null when no customer carries the reference.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json — fails with a "must be unique" 422 if the reference is taken.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes attributes, CancellationToken cancellationToken = default);

    /// <summary>GET /subscriptions/lookup.json — returns null when no subscription carries the reference.</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json, submitted with a uniqueness token so it can be retried safely.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes attributes, string uniquenessToken, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{id}/subscriptions.json — every subscription belonging to a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
