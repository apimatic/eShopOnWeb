using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Low-level, spec-driven client for the subset of the Maxio Advanced Billing OpenAPI contract
/// required to run the subscription flow. Each method maps one-to-one onto an operation in
/// <c>maxio-spec/openapi.yaml</c>. 404 responses are surfaced as <c>null</c> returns; other
/// non-success statuses throw <see cref="MaxioApiException"/>.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /products.json (operation <c>listProducts</c>) — all plans on the site.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListProductsAsync(CancellationToken ct = default);

    /// <summary>GET /customers/lookup.json (operation <c>readCustomerByReference</c>); null when not found.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default);

    /// <summary>POST /customers.json (operation <c>createCustomer</c>).</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput input, CancellationToken ct = default);

    /// <summary>GET /subscriptions/lookup.json (operation <c>findSubscription</c>); null when not found.</summary>
    Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct = default);

    /// <summary>POST /subscriptions.json (operation <c>createSubscription</c>).</summary>
    Task<Subscription> CreateSubscriptionAsync(MaxioSubscriptionInput input, CancellationToken ct = default);

    /// <summary>GET /customers/&#123;customer_id&#125;/subscriptions.json (operation <c>listCustomerSubscriptions</c>).</summary>
    Task<IReadOnlyList<Subscription>> ListSubscriptionsForCustomerAsync(long customerId, CancellationToken ct = default);
}
