using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);
    Task<MaxioProduct?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerPayload customer, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionPayload subscription, CancellationToken cancellationToken = default);
}
