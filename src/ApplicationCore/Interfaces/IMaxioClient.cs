using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin abstraction over the Maxio Advanced Billing API. Implementations are responsible for
/// idempotency at the HTTP boundary (e.g. Maxio's unique customer reference constraint).
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// Returns the Maxio customer for <paramref name="reference"/>, creating one if none exists yet.
    /// Safe to call concurrently for the same reference: Maxio enforces reference uniqueness, and a
    /// racing create is resolved by re-reading the customer that won.
    /// </summary>
    Task<MaxioCustomerDto> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscribable plans (Products) in the configured Product Family.
    /// </summary>
    Task<IReadOnlyList<MaxioPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
