using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, typed client over the Maxio Advanced Billing HTTP API. Every method maps
/// to exactly one operation defined in the OpenAPI spec under <c>maxio-spec/</c>.
/// Non-success responses (other than an expected 404 on lookup) surface as
/// <see cref="MaxioApiException"/>.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// GET <c>/product_families/{product_family_id}/products.json</c> using the family
    /// handle (prefixed with <c>handle:</c>). Returns the products of the family.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET <c>/customers/lookup.json?reference=...</c>. Returns the matching customer,
    /// or <c>null</c> when no customer has that reference (HTTP 404).
    /// </summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST <c>/customers.json</c> to create a customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>GET <c>/customers/{customer_id}/subscriptions.json</c>.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET <c>/subscriptions/lookup.json?reference=...</c>. Returns the subscription with
    /// that app reference, or <c>null</c> when none exists (HTTP 404).
    /// </summary>
    Task<MaxioSubscription?> LookupSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST <c>/subscriptions.json</c> to create a subscription.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken = default);
}
