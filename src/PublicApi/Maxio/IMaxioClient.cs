using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed client for the Maxio Advanced Billing (Chargify) REST API.
/// Only the operations needed by the subscription capability are exposed;
/// Maxio remains the billing system of record.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// Reads a product family by its stable handle.
    /// Returns null when no family with that handle exists.
    /// </summary>
    Task<MaxioProductFamily?> GetProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the (non-archived) products offered under the given product family handle.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a Maxio customer by its external reference (we use the eShopOnWeb user id).
    /// Returns null when no customer carries that reference.
    /// </summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions belonging to a Maxio customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription. When <see cref="MaxioCreateSubscriptionBody.CustomerId"/>
    /// is null, <see cref="MaxioCreateSubscriptionBody.CustomerAttributes"/> must be set
    /// and the Maxio customer is created atomically with the subscription.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default);
}
