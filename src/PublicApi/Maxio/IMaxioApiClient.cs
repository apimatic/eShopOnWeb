using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin client over the Maxio Advanced Billing HTTP API (formerly Chargify).
/// Authentication is HTTP Basic with the API key as username and the literal password "x";
/// the API base address is https://{subdomain}.chargify.com unless overridden.
/// </summary>
public interface IMaxioApiClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductFamilyProductsAsync(string productFamilyHandle, CancellationToken cancellationToken);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}
