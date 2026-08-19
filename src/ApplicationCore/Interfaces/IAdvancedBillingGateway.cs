using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port for the Maxio Advanced Billing system of record. Method names and payloads
/// map to the OpenAPI operations in maxio-spec (listProductsForProductFamily,
/// readCustomerByReference, createCustomer, listCustomerSubscriptions,
/// findSubscription, createSubscription).
/// </summary>
public interface IAdvancedBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(CreateBillingCustomer customer, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<ShopperSubscription> CreateSubscriptionAsync(CreateBillingSubscription subscription, CancellationToken cancellationToken = default);
}
