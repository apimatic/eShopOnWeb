using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Verified Maxio Advanced Billing operations used by eShopOnWeb.
/// HTTP mapping: Basic auth (API key as username, "x" as password) against
/// GET /product_families/handle:{handle}/products.json,
/// GET /customers/lookup.json?reference=, POST /customers.json,
/// GET /customers/{id}/subscriptions.json,
/// GET /subscriptions/lookup.json?reference=, POST /subscriptions.json.
/// </summary>
public interface IMaxioClient
{
    Task<IReadOnlyList<SubscriptionPlan>> ListProductsForConfiguredFamilyAsync(CancellationToken cancellationToken = default);

    Task<BillingCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    Task<ShopperSubscription?> GetSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<ShopperSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
