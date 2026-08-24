using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed client for the subset of the Maxio Advanced Billing API this integration uses.
/// </summary>
internal interface IMaxioClient
{
    /// <summary>Lists the (non-archived) products of a product family, addressed by the family's API handle.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>Finds a customer by its unique reference. Returns null when no such customer exists.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates a customer. The reference must be unique; Maxio returns 422 otherwise.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates a subscription for an existing customer (by reference) to a product (by handle), billed by invoice (remittance).</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions belonging to a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
