using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Client for the Maxio Advanced Billing HTTP API. The contract for every
/// operation is the Maxio OpenAPI specification (maxio-spec/openapi.yaml).
/// </summary>
public interface IMaxioBillingClient
{
    /// <summary>
    /// GET /product_families/{product_family_id}/products.json — the family is
    /// addressed by handle using the spec's "handle:{handle}" path format.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /customers/lookup.json?reference={reference}. Returns null when no
    /// customer exists for the reference (404).
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// POST /customers.json. The reference must be unique per the spec.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /customers/{customer_id}/subscriptions.json.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsByCustomerAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// POST /subscriptions.json for an existing customer identified by reference.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, CancellationToken cancellationToken = default);
}
