using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public interface IMaxioClient
{
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken ct = default);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken ct = default);
    Task<List<MaxioProduct>> ListProductsAsync(string? productFamilyHandle = null, CancellationToken ct = default);
    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default);
    Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken ct = default);
}
