using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Gateway to Maxio Advanced Billing. Maxio is the system of record for customers and subscriptions.
/// </summary>
public interface IMaxioBillingClient
{
    Task<IReadOnlyList<SubscriptionPlan>> ListProductsInFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(
        CreateMaxioCustomerRequest request,
        CancellationToken cancellationToken = default);

    Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    Task<ShopperSubscription> CreateSubscriptionAsync(
        CreateMaxioSubscriptionRequest request,
        CancellationToken cancellationToken = default);
}
