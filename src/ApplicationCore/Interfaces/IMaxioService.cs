using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin client for the Maxio Advanced Billing API. Implementations talk over HTTP;
/// this port carries no eShopOnWeb business rules (see <see cref="ISubscriptionService"/> for those).
/// </summary>
public interface IMaxioService
{
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns null when no customer exists for the given reference.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(NewMaxioCustomer newCustomer, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string productHandle, CancellationToken cancellationToken = default);
}
