using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin client over the Maxio Advanced Billing REST API (the Chargify-style
/// <c>.json</c> endpoints). Authentication is HTTP Basic with the API key as the user name
/// and <c>X</c> as the password, per the Maxio documentation.
/// </summary>
public interface IMaxioBillingGateway
{
    /// <summary>Lists the products (plans) that belong to a product family, by family handle.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsByFamilyHandleAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds a customer by its unique <c>reference</c> value. Returns <c>null</c> when no
    /// customer carries that reference.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken);

    /// <summary>Creates a customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(
        MaxioNewCustomer customer,
        CancellationToken cancellationToken);

    /// <summary>Lists every subscription that belongs to a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(
        long customerId,
        CancellationToken cancellationToken);

    /// <summary>Creates a subscription for an existing customer and product.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioNewSubscription subscription,
        CancellationToken cancellationToken);
}
