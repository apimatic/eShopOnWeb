using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioClient
{
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);
    Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken);
    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomer customer, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscription subscription, CancellationToken cancellationToken);
}
