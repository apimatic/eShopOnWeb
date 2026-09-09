using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level operations against the Maxio (Advanced Billing) REST API used by this integration.
/// Kept internal to Infrastructure; the orchestration lives in <see cref="MaxioSubscriptionService"/>.
/// </summary>
internal interface IMaxioApiClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default);

    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerInput input, CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionInput input, string? uniquenessToken = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
