using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, spec-faithful client over the Maxio Advanced Billing REST API. Each method maps to exactly one
/// operation in the OpenAPI spec (maxio-spec/openapi.yaml). Non-success responses raise
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.MaxioBillingException"/>.
/// </summary>
internal interface IMaxioApiClient
{
    /// <summary>GET /product_families/{product_family_id}/products.json — products in a family (id may be `handle:{handle}`).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference=... — returns null when no customer matches (404).</summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json — create a customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody customer, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json — all subscriptions for a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json — create a subscription.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody subscription, CancellationToken cancellationToken = default);
}
