using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<Product>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken);
    Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<Customer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken);
    Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
    Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<Subscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken);
}
