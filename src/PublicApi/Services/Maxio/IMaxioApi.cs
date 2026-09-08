using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Services.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Services.Maxio;

public interface IMaxioApi
{
    Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(CancellationToken cancellationToken);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerDraft customer, CancellationToken cancellationToken);

    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionDraft subscription, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
}
