using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin async wrapper over the Maxio Advanced Billing HTTP API.
/// Every method maps to an operation in maxio-spec/openapi.yaml.
/// </summary>
public interface IMaxioClient
{
    /// <summary>listProducts — GET /products.json (paged).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>readCustomerByReference — GET /customers/lookup.json?reference=... Returns null on 404.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>createCustomer — POST /customers.json.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default);

    /// <summary>listCustomerSubscriptions — GET /customers/{customer_id}/subscriptions.json.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>createSubscription — POST /subscriptions.json.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string paymentCollectionMethod, CancellationToken cancellationToken = default);
}
