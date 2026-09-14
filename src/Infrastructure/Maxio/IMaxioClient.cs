using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public interface IMaxioClient
{
    Task<MaxioCustomerEnvelope?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomerEnvelope> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioProductFamilyEnvelope>> ListProductFamiliesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioProductEnvelope>> ListProductsForFamilyAsync(int productFamilyId, CancellationToken cancellationToken = default);

    Task<MaxioSubscriptionEnvelope?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioSubscriptionEnvelope> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscriptionEnvelope>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);
}
