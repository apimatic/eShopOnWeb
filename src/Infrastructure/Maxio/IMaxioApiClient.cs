using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Thin, typed wrapper around the subset of the Maxio Advanced Billing REST API this app uses.</summary>
public interface IMaxioApiClient
{
    /// <summary>GET /customers/lookup.json?reference=... Returns null on 404 (no such customer).</summary>
    Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /customers.json</summary>
    Task<Customer> CreateCustomerAsync(CreateCustomerAttributes attributes, CancellationToken cancellationToken);

    /// <summary>GET /products.json (all products across every product family on the site).</summary>
    Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken cancellationToken);

    /// <summary>GET /customers/{customerId}/subscriptions.json</summary>
    Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>POST /subscriptions.json</summary>
    Task<Subscription> CreateSubscriptionAsync(CreateSubscriptionAttributes attributes, CancellationToken cancellationToken);
}
