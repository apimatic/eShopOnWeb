using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, transport-level client over the Maxio Advanced Billing REST API. Each method maps to
/// a single verified Maxio endpoint and returns the parsed resource; orchestration (idempotency,
/// mapping to domain types) lives in <see cref="MaxioSubscriptionService"/>.
/// </summary>
internal interface IMaxioApiClient
{
    Task<IReadOnlyList<MaxioProductFamily>> GetProductFamiliesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioProduct>> GetProductsByFamilyAsync(int productFamilyId, CancellationToken cancellationToken);

    /// <summary>Returns the customer with the given reference, or <c>null</c> if none exists (HTTP 404).</summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(CustomerInput input, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(SubscriptionInput input, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
}
