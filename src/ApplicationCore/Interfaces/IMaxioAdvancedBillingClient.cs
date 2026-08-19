using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Typed client for the Maxio Advanced Billing operations this app uses.
/// Paths, payloads, and auth match the OpenAPI contract in <c>maxio-spec/</c>.
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<SubscriptionPlan>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    Task<SubscriptionPlan?> ReadProductByHandleAsync(
        string productHandle,
        CancellationToken cancellationToken = default);

    Task<BillingCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingCustomer>> ListCustomersAsync(
        string query,
        CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(
        BillingCustomer customer,
        CancellationToken cancellationToken = default);

    Task<BillingCustomer> UpdateCustomerAsync(
        int customerId,
        BillingCustomer customer,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    Task<ShopperSubscription> CreateSubscriptionAsync(
        string productHandle,
        int customerId,
        string? subscriptionReference,
        CancellationToken cancellationToken = default);
}
