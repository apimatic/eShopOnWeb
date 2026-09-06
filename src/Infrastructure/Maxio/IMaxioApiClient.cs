using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, faithful binding of the Maxio Advanced Billing REST endpoints this integration uses.
/// It performs no business decisions: no idempotency, no adoption, no state filtering. Those live in
/// <see cref="MaxioSubscriptionBillingService"/>, which keeps the orchestration testable against a
/// substitute of this interface.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET <c>/site.json</c> — site metadata, used for the site's primary currency.</summary>
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// GET <c>/product_families/handle:{handle}/products.json</c> — every product in the family,
    /// following pagination. Maxio accepts <c>handle:</c>-prefixed identifiers in place of numeric
    /// ids, which is what keeps this integration working across re-seeds.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET <c>/customers/lookup.json?reference=</c> — returns <c>null</c> when Maxio answers 404,
    /// which is how it reports "no such customer".
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST <c>/customers.json</c>. Fails with 422 if the reference is already taken.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default);

    /// <summary>GET <c>/customers/{id}/subscriptions.json</c>, following pagination.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>POST <c>/subscriptions.json</c>. Fails with 422 if the reference is already taken.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET <c>/subscriptions/lookup.json?reference=</c> — returns <c>null</c> when Maxio answers 404.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);
}
