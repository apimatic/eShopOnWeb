using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.MaxioModels;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioBillingService
{
    Task<Customer> GetOrCreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        CancellationToken ct = default);

    Task<List<Product>> GetProductsAsync(
        CancellationToken ct = default);

    Task<Subscription> CreateSubscriptionAsync(
        long customerId,
        long productId,
        CancellationToken ct = default);

    Task<Subscription> GetSubscriptionAsync(
        long subscriptionId,
        CancellationToken ct = default);

    Task<List<Subscription>> GetCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken ct = default);
}
