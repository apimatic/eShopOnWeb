using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.ExternalServices.Maxio.Wire;

namespace Microsoft.eShopWeb.Infrastructure.ExternalServices.Maxio;

/// <summary>
/// The subset of the Maxio Advanced Billing API (see maxio-spec/openapi.yaml) needed for
/// subscription enrollment. Extracted as an interface purely so <see cref="MaxioSubscriptionService"/>'s
/// idempotency logic can be unit tested without a live HTTP dependency.
/// </summary>
public interface IMaxioApiClient
{
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken);
}
