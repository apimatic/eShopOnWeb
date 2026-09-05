using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin wrapper over the Maxio Advanced Billing HTTP API. Every member maps directly to an
/// operation described in the maxio-spec OpenAPI document - it does not add business rules.
/// </summary>
public interface IMaxioBillingClient
{
    /// <summary>
    /// Lists the (non-archived) products/plans defined on the given product family.
    /// GET /product_families/handle:{productFamilyHandle}/products.json
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductFamilyProductsAsync(string productFamilyHandle);

    /// <summary>
    /// Looks up a customer by the reference eShopOnWeb assigned it. Returns null when no
    /// customer exists for that reference.
    /// GET /customers/lookup.json?reference={reference}
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference);

    /// <summary>
    /// Creates a new Maxio customer. POST /customers.json
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer);

    /// <summary>
    /// Lists every subscription that belongs to the given Maxio customer.
    /// GET /customers/{customer_id}/subscriptions.json
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId);

    /// <summary>
    /// Enrolls an existing customer (by reference) into a product/plan. POST /subscriptions.json
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate request);
}
