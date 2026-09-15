using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Models;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public interface IMaxioAdvancedBillingClient
{
    Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);
    Task<Customer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);
    Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);
    Task<Subscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
