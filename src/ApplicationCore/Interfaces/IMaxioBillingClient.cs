using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin Maxio Advanced Billing gateway. Maxio is the system of record for customers and subscriptions.
/// </summary>
public interface IMaxioBillingClient
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(
        ShopperIdentity shopper,
        string uniquenessToken,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string subscriptionReference,
        string uniquenessToken,
        CancellationToken cancellationToken = default);

    Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);
}
