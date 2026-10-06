using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin transport-level client for the Maxio Advanced Billing REST API.
/// Lookup methods return null on HTTP 404 (resource not found); any other
/// error raises <see cref="MaxioApiException"/>.
/// </summary>
public interface IMaxioClient
{
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest customer, CancellationToken cancellationToken = default);

    Task<MaxioProductFamily?> FindProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(int productFamilyId, CancellationToken cancellationToken = default);

    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}