using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Thin client for the Maxio Advanced Billing API. All Maxio-specific knowledge is confined
/// behind this interface so higher layers (and tests) never need to talk HTTP.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>Looks up a customer by the unique reference used by the calling app.</summary>
    /// <returns>The matching customer, or <c>null</c> when no customer has that reference.</returns>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>Creates a customer. Throws <see cref="MaxioApiException"/> when the reference is already taken.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreateRequest request, CancellationToken cancellationToken);

    /// <summary>Lists the non-archived subscription plans (products) in the configured product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(string productFamilyHandle, CancellationToken cancellationToken);

    /// <summary>Lists all subscriptions belonging to a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>Creates a subscription for an existing customer / product.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreateRequest request, CancellationToken cancellationToken);

    /// <summary>Reads site-level settings (currency, billing architecture). <c>null</c> when it cannot be determined.</summary>
    Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken);
}
