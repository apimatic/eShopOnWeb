using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed operations against Maxio Advanced Billing, built strictly on the
/// contract in maxio-spec/openapi.yaml.
/// </summary>
public interface IMaxioClient
{
    /// <summary>Read Customer by Reference — GET /customers/lookup.json?reference=.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>Create Customer — POST /customers.json.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken);

    /// <summary>List Products for Product Family — GET /product_families/handle:{handle}/products.json (paged).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductFamilyProductsAsync(string productFamilyHandle, CancellationToken cancellationToken);

    /// <summary>Create Subscription — POST /subscriptions.json.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken);

    /// <summary>Find Subscription — GET /subscriptions/lookup.json?reference=.</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
}
