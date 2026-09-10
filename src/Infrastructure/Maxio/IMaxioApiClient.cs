using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, spec-faithful HTTP client over the subset of the Maxio Advanced Billing API required by
/// the subscription-billing capability. Each method maps to exactly one operation in the OpenAPI
/// contract under <c>maxio-spec/</c>.
/// </summary>
internal interface IMaxioApiClient
{
    /// <summary>
    /// <c>GET /product_families/handle:{handle}/products.json</c> (listProductsForProductFamily).
    /// Returns every non-archived product in the family, paging through results.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET /customers/lookup.json?reference={reference}</c> (readCustomerByReference).
    /// Returns <c>null</c> when no customer matches the reference.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary><c>POST /customers.json</c> (createCustomer).</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody customer, CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET /customers/{customer_id}/subscriptions.json</c> (listCustomerSubscriptions).
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    /// <summary><c>POST /subscriptions.json</c> (createSubscription).</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody subscription, CancellationToken cancellationToken);
}
