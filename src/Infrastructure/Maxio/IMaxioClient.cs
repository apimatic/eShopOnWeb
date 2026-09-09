using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin typed client over the subset of the Maxio Advanced Billing API used by the subscription integration.
/// Every method maps 1:1 to an operation defined in the Maxio OpenAPI spec (maxio-spec/openapi.yaml).
/// Non-success responses surface as <see cref="MaxioApiException"/>.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// GET <c>/product_families/{product_family_id}/products.json</c> (operationId listProductsForProductFamily),
    /// addressing the family by handle (<c>handle:{familyHandle}</c>).
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET <c>/customers/lookup.json?reference=</c> (operationId readCustomerByReference).
    /// Returns <c>null</c> when no customer has the given reference (HTTP 404).
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST <c>/customers.json</c> (operationId createCustomer).</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody customer, CancellationToken cancellationToken = default);

    /// <summary>GET <c>/customers/{customer_id}/subscriptions.json</c> (operationId listCustomerSubscriptions).</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>POST <c>/subscriptions.json</c> (operationId createSubscription).</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody subscription, CancellationToken cancellationToken = default);
}
