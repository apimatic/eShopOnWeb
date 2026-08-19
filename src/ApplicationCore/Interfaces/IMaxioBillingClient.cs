using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Typed client for the Maxio Advanced Billing REST API, built against the OpenAPI spec in <c>maxio-spec/</c>.
/// </summary>
public interface IMaxioBillingClient
{
    /// <summary>GET /product_families/{product_family_id}/products.json</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListProductsForProductFamilyAsync(CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference=</summary>
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>GET /subscriptions/lookup.json?reference=</summary>
    Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string? subscriptionReference,
        CancellationToken cancellationToken = default);
}
