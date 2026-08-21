using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Typed client for the Maxio Advanced Billing operations used by subscription billing.
/// Method names and shapes follow the OpenAPI spec in <c>maxio-spec/</c>.
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    string ProductFamilyHandle { get; }

    Task<IReadOnlyList<SubscriptionPlan>> ListProductsForProductFamilyAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionPlan?> ReadProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default);

    Task<BillingCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<ShopperSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, string paymentCollectionMethod, CancellationToken cancellationToken = default);
}
