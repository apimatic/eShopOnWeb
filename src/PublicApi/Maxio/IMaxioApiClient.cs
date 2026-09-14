using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin, Maxio Advanced Billing REST client. All Maxio traffic flows through
/// this interface so the rest of the app does not know the API shape.
/// </summary>
public interface IMaxioApiClient
{
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioProduct>> ListProductFamilyProductsAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string customerReference,
        CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(
        string customerReference,
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default);

    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string subscriptionReference,
        CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken = default);
}
