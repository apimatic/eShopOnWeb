using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioBillingClient
{
    Task<MaxioProductFamily?> GetProductFamilyByHandleAsync(string productFamilyHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioProduct>> ListProductsByFamilyHandleAsync(string productFamilyHandle, CancellationToken cancellationToken);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken);

    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string paymentCollectionMethod, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
}
