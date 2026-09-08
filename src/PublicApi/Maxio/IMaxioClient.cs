using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest customer, CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest subscription, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
