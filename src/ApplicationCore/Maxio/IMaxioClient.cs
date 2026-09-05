using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Client for the Maxio Advanced Billing REST API operations required by eShopOnWeb's
/// subscription-billing capability.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// Lists the (non-archived) plans in the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a customer by external reference. Returns null if none exists yet.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the customer for the given reference, creating it first if it doesn't exist yet.
    /// Idempotent: concurrent callers racing to create the same reference converge on one customer.
    /// </summary>
    Task<MaxioCustomer> FindOrCreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
