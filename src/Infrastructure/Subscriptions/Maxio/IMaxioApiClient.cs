using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Thin, one-to-one wrapper over the Maxio Advanced Billing REST endpoints used by this integration.
/// Each method maps directly to an operation in the OpenAPI spec (the authoritative contract).
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// <c>GET /product_families/handle:{familyHandle}/products.json</c> — lists the (non-archived) products
    /// in a product family, resolved by handle.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(string familyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /customers/lookup.json?reference={reference}</c> — returns the single customer matching the
    /// reference, or <c>null</c> when none exists (the spec returns 404 for no match).
    /// </summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>POST /customers.json</c> — creates a customer and returns it.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /customers/{customerId}/subscriptions.json</c> — lists all subscriptions belonging to a customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary><c>POST /subscriptions.json</c> — creates a subscription and returns it.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody subscription, CancellationToken cancellationToken = default);
}
