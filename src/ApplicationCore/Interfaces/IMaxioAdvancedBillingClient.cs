using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Maxio Advanced Billing operations used by eShopOnWeb, mapped 1:1 to the OpenAPI contract.
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<SubscriptionPlan>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    Task<BillingCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(
        CreateBillingCustomerRequest request,
        CancellationToken cancellationToken = default);

    Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<ShopperSubscription> CreateSubscriptionAsync(
        CreateBillingSubscriptionRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);
}
