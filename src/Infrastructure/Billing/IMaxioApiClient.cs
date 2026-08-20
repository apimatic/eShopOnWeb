using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Thin HTTP surface over Maxio Advanced Billing (formerly Chargify).
/// Endpoints verified against the official Advanced Billing API docs.
/// </summary>
public interface IMaxioApiClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default);
}
