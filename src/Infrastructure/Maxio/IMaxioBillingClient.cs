using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level client for the Maxio Advanced Billing API (https://{site}.chargify.com),
/// authenticated with HTTP Basic (API key as username, "X" as password).
/// </summary>
public interface IMaxioBillingClient
{
    Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all products on the site (paged) and returns those that belong to the
    /// given product family handle and are not archived.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default);

    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreateRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports whether the site uses Relationship Invoicing (determines the valid
    /// payment_collection_method values: "remittance"/"automatic"/"prepaid" for RI
    /// sites, "invoice"/"automatic" for legacy sites). Cached per site.
    /// </summary>
    Task<bool> IsRelationshipInvoicingEnabledAsync(CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string paymentCollectionMethod, string uniquenessToken, CancellationToken cancellationToken = default);

    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}