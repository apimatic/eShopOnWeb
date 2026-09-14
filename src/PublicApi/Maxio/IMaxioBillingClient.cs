using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioBillingClient
{
    Task<SiteData> GetSiteAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerWrite customer, string? uniquenessToken, CancellationToken cancellationToken = default);

    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionWrite subscription, string? uniquenessToken, CancellationToken cancellationToken = default);
}
