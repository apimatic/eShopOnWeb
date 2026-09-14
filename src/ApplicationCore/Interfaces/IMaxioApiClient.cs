using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin wrapper over the Maxio Billing API's HTTP surface. Contains no business rules -
/// see <see cref="IMaxioSubscriptionService"/> for the orchestration (idempotent customer
/// provisioning, subscribe rules, etc.) built on top of this client.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// Looks up a customer by their application-supplied reference. Returns null if no customer
    /// with that reference exists yet.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
