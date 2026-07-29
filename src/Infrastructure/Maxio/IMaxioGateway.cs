using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin wrapper over the Maxio Advanced Billing (Chargify) REST API. Each member maps to a single
/// endpoint verified against the live sandbox. Higher-level orchestration (idempotency, plan
/// validation) lives in <see cref="MaxioSubscriptionService"/>.
/// </summary>
internal interface IMaxioGateway
{
    /// <summary>GET /product_families/{id}/products.json — lists products in the family (resolved from its handle).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken);

    /// <summary>GET /customers/lookup.json?reference=... — returns null when no customer matches.</summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /customers.json — creates a customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken);

    /// <summary>GET /customers/{id}/subscriptions.json — lists a customer's subscriptions.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>POST /subscriptions.json — subscribes an existing customer to a product by handle (remittance collection).</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken);
}
