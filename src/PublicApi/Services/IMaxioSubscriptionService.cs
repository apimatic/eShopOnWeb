using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Models;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioSubscriptionService
{
    Task<(Customer customer, bool isNew)> GetOrCreateCustomerAsync(
        string customerReference, string firstName, string lastName, string email, CancellationToken ct);

    Task<Subscription> CreateSubscriptionAsync(
        int customerId, string productHandle, string? subscriptionReference, CancellationToken ct);

    Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken ct);

    Task<IReadOnlyList<Product>> ListSubscriptionProductsAsync(CancellationToken ct);
}
