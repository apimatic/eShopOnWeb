using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreateRequest request, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
