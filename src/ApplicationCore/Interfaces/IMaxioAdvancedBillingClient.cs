using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// HTTP client for Maxio Advanced Billing (Chargify). Maxio is the system of record.
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<SubscriptionPlan>> ListProductsForConfiguredFamilyAsync(
        CancellationToken cancellationToken = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(
        NewBillingCustomer customer,
        CancellationToken cancellationToken = default);

    Task<ShopperSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string uniquenessToken,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);
}
