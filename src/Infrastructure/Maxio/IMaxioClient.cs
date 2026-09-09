using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, typed client over the Maxio Advanced Billing REST API.
/// Endpoints used (documented at docs.maxio.com):
///  - GET  /customers/lookup.json?reference=       (Read Customer by Reference)
///  - POST /customers.json                         (Create Customer)
///  - GET  /product_families/handle:{x}/products.json (List Products for Product Family)
///  - POST /subscriptions.json                     (Create Subscription)
///  - GET  /customers/{id}/subscriptions.json      (List Customer Subscriptions)
/// </summary>
public interface IMaxioClient
{
    /// <summary>Returns the customer with the given reference, or null when none exists.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken = default);

    /// <summary>Lists all (non-archived) products in the given product family, following pagination.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionInput input, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions belonging to the given Billing API customer, following pagination.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
