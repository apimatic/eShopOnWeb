using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Port to the Maxio Advanced Billing JSON API. Endpoint paths, auth scheme and
/// payload shapes were all verified against the live Advanced Billing sandbox:
///   - Basic auth: API key as username, literal "x" as password.
///   - GET  /product_families.json
///   - GET  /product_families/{id}/products.json
///   - GET  /customers/lookup.json?reference={reference}
///   - POST /customers.json  (customer reference must be unique site-wide)
///   - POST /subscriptions.json  (payment_collection_method=remittance requires no stored card)
///   - GET  /customers/{id}/subscriptions.json
///   - GET  /subscriptions/{id}.json
/// </summary>
public interface IMaxioApiClient
{
    Task<MaxioProductFamily?> FindProductFamilyAsync(string handle, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(int productFamilyId, CancellationToken cancellationToken);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken);
}
