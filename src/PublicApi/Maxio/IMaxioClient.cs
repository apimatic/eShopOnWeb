using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Client for the Maxio Advanced Billing API, the billing system of record
/// for eShopOnWeb subscription plans.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// Resolves a product family by its handle. Returns null when no family matches.
    /// </summary>
    Task<MaxioProductFamily?> GetProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the products (plans) in a product family.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> GetProductsByFamilyAsync(long productFamilyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a customer by the site-unique reference (the eShopOnWeb username).
    /// Returns null when no customer matches.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the customer with the given reference, creating one when none exists.
    /// Idempotent: safe to call concurrently or repeatedly for the same reference.
    /// </summary>
    Task<MaxioCustomer> GetOrCreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions belonging to a customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription to the given product, billed by invoice (remittance)
    /// so that no payment method is required at signup.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string? reference, CancellationToken cancellationToken = default);
}
