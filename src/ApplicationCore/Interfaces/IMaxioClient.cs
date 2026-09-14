using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Client abstraction over the Maxio Advanced Billing API surface needed to support
/// subscription billing. Maxio is the system of record for customers and subscriptions;
/// this app does not persist a local copy of that state.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// Looks up a Maxio customer by its <c>reference</c> (the eShopOnWeb user's email).
    /// Returns null when no customer has been created for that reference yet.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new Maxio customer with the given reference. Throws <see cref="Exceptions.MaxioApiException"/>
    /// if the reference is already taken (callers should treat this as a signal to re-fetch).
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscribable plans (products) in the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions belonging to the given Maxio customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new subscription for the given customer to the given plan handle.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default);
}
