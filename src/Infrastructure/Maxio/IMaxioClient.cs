using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level, typed access to the Maxio Advanced Billing (Chargify) REST API. Wire shapes only,
/// no business logic. Extracted as an interface so the orchestrating service can be unit-tested.
/// </summary>
public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioProductFamily>> GetProductFamiliesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<MaxioProduct>> GetProductsInFamilyAsync(long familyId, CancellationToken ct = default);

    /// <summary>Returns null when no customer has the given reference (HTTP 404).</summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken ct = default);

    Task<MaxioCustomer> CreateCustomerAsync(CustomerAttributes attributes, CancellationToken ct = default);

    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken ct = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(SubscriptionAttributes attributes, CancellationToken ct = default);
}
