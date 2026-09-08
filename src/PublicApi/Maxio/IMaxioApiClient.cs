using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client over the Maxio Advanced Billing (Chargify) REST API.
/// Every method talks JSON to the endpoints documented by Maxio and verified
/// against the sandbox; non-success responses surface as <see cref="MaxioApiException"/>.
/// </summary>
public interface IMaxioApiClient
{
    Task<IReadOnlyList<ProductFamilyDto>> ListProductFamiliesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken);
}
