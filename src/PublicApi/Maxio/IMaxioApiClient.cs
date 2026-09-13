using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioApiClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken ct = default);
    Task<MaxioCustomer?> FindCustomerByEmailAsync(string email, CancellationToken ct = default);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreateRequest request, CancellationToken ct = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreateRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsByCustomerIdAsync(int customerId, CancellationToken ct = default);
}
