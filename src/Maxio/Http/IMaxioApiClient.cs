using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Maxio.Http;

/// <summary>
/// Thin, typed HTTP client over the Maxio Advanced Billing REST API. Every endpoint,
/// request payload and response field used here was verified against a live Maxio
/// Advanced Billing sandbox (and cross-checked with the official Advanced Billing SDK).
/// </summary>
public interface IMaxioApiClient
{
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioProduct>> ListProductsByFamilyAsync(long productFamilyId, CancellationToken cancellationToken);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioNewCustomer customer, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioNewSubscription subscription, CancellationToken cancellationToken);
}
