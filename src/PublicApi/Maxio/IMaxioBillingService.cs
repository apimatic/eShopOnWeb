using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Outcome of an idempotent subscribe attempt. When <see cref="WasCreated"/> is false the
/// returned subscription already existed (e.g. a double-click replayed the request).
/// </summary>
public sealed class MaxioSubscribeResult
{
    public MaxioSubscription Subscription { get; init; } = new();
    public bool WasCreated { get; init; }
}

/// <summary>
/// Application service that talks to Maxio Advanced Billing following the Maxio OpenAPI
/// specification (maxio-spec/openapi.yaml) as the authoritative contract.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>Lists the subscribable plans (products) of the configured product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all Maxio subscriptions that belong to the Maxio customer mapped to the
    /// given eShopOnWeb user. An empty list is returned when the user has no customer yet.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(string identity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the Maxio customer for the user when it does not already exist
    /// (idempotent). The user is identified by a stable reference derived from
    /// <paramref name="identity"/> so a double click never yields two customers.
    /// </summary>
    Task<MaxioCustomer> GetOrCreateCustomerAsync(string identity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the user to the given plan handle. Idempotent: when the user already has
    /// a subscription to that plan the existing subscription is returned.
    /// </summary>
    Task<MaxioSubscribeResult> SubscribeAsync(string identity, string productHandle, CancellationToken cancellationToken = default);
}
