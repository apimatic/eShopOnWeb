using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level access to the Maxio Advanced Billing JSON API. All shapes verified against
/// the Maxio sandbox; see MaxioModels.cs.
/// </summary>
public interface IMaxioClient
{
    /// <summary>Returns all non-archived products visible on the site.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a customer by its reference (our stable user identifier).
    /// Returns null when no customer has that reference (API responds 404).
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates a customer. Throws <see cref="MaxioApiException"/> on validation errors.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription for an existing customer. Uses the invoicing collection
    /// method so signup succeeds without payment-method capture.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, int productId, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions belonging to a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
