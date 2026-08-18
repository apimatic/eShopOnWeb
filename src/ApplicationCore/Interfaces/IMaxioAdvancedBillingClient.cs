using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Maxio Advanced Billing operations required by eShopOnWeb subscription flows.
/// Paths, query parameters, and payloads match the Maxio OpenAPI specification.
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<SubscriptionPlan>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(CreateBillingCustomer customer, CancellationToken cancellationToken = default);

    Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<ShopperSubscription> CreateSubscriptionAsync(CreateBillingSubscription subscription, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
