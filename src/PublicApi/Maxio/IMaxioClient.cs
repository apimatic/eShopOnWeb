using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin, Maxio-shape client for the subset of the Advanced Billing (Chargify) API that the
/// subscription capability needs. All methods throw <see cref="MaxioApiException"/> when the
/// upstream call fails, except <see cref="FindCustomerByReferenceAsync"/> which returns
/// <c>null</c> when no customer has the reference yet.
/// </summary>
public interface IMaxioClient
{
    /// <summary>Lists the products (plans) of the configured product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);

    /// <summary>Returns the customer with the given reference, or null when none exists yet.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>Creates a customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomer customer, CancellationToken cancellationToken);

    /// <summary>Lists the subscriptions that belong to the given customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);

    /// <summary>Creates a subscription for an existing customer at the product's default price point.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken);
}
