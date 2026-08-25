using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed client for the Maxio Advanced Billing API. Maxio is the billing system of record;
/// all subscription state is read from and written to Maxio through this abstraction.
/// </summary>
public interface IMaxioClient
{
    /// <summary>Lists the (non-archived) products in the configured product family — the subscribable plans.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds a customer by the application-owned reference value. Returns null when no match exists.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates a customer. The reference value must be unique per customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default);

    /// <summary>Creates a subscription for an existing customer to a product identified by its handle.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string? reference, string uniquenessToken, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions belonging to a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
