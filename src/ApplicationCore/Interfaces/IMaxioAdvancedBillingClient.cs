using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioAdvancedBillingClient
{
    /// <summary>
    /// Lists the plans (products) of the given product family.
    /// GET /products.json?filter[product_family_id]={id}, with the family resolved by handle.
    /// </summary>
    Task<IReadOnlyList<MaxioPlan>> GetPlansForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a customer by its application reference, or null when none exists.
    /// GET /customers/lookup.json?reference={reference}
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer. Maxio enforces uniqueness of the reference value, so this is
    /// idempotency-safe: a duplicate reference fails with
    /// <see cref="Exceptions.MaxioBillingProviderException.DuplicateCustomerReference"/> set.
    /// POST /customers.json
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string? email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions of a customer.
    /// GET /subscriptions.json?customer_id={id}
    /// </summary>
    Task<IReadOnlyList<MaxioSubscriptionInfo>> GetSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a customer in a plan without requiring a payment method
    /// (invoice-based collection, matching the seeded plans' "payment method not required" setting).
    /// POST /subscriptions.json
    /// </summary>
    Task<MaxioSubscriptionInfo> CreateSubscriptionAsync(int customerId, MaxioPlan plan, CancellationToken cancellationToken = default);
}
