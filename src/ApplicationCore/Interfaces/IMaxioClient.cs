using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Client for the Maxio Advanced Billing API.
/// </summary>
public interface IMaxioClient
{
    /// <summary>Finds a customer by its unique reference, or null when it does not exist.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Reads a customer by its Maxio id, or null when it does not exist.</summary>
    Task<MaxioCustomer?> GetCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Creates a customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Creates a subscription for an existing customer.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, string uniquenessToken, CancellationToken cancellationToken = default);

    /// <summary>Finds a subscription by its unique reference, or null when it does not exist.</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Reads a subscription by its Maxio id, or null when it does not exist.</summary>
    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions that belong to a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Lists the products (plans) that belong to a product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>Reads a product (plan) by its API handle, or null when it does not exist.</summary>
    Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default);
}
