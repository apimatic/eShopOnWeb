using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Client for the subset of the Maxio Advanced Billing API used by the subscription capability.
/// </summary>
public interface IMaxioBillingClient
{
    /// <summary>Lists the plans (products) of a product family. GET /product_families/handle:{handle}/products.json</summary>
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>Finds a customer by the application-provided reference, or null when none exists.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates a customer. The reference must be unique across the site.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(SubscriberProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions that belong to a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls an existing customer in a plan. <paramref name="uniquenessToken"/> makes the call
    /// duplicate-safe: a repeated POST with the same token within the Maxio de-dup window is
    /// rejected with 409 (see about-the-api/duplicate-prevention).
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string planHandle, string? subscriptionReference, string uniquenessToken, CancellationToken cancellationToken = default);
}
