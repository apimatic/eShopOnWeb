using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Talks to Maxio Advanced Billing (the billing system of record) for the subscription
/// capability. Implemented in Infrastructure over HTTP.
/// </summary>
public interface IMaxioBillingService
{
    Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up an existing customer by their unique reference (the eShopOnWeb user id).
    /// Returns null when no customer with that reference exists yet. Read-only, no side effects.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the customer for <paramref name="reference"/>, or creates one if none exists yet.
    /// Idempotent: calling this repeatedly for the same reference never creates duplicate customers.
    /// </summary>
    Task<MaxioCustomer> GetOrCreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForCustomerAsync(int maxioCustomerId, CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(int maxioCustomerId, string productHandle, CancellationToken cancellationToken = default);
}
