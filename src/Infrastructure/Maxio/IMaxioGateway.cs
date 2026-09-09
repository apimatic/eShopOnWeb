using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level access to the Maxio Advanced Billing REST API.
/// Every call made by this interface has been verified against the live
/// sandbox and the official Maxio Advanced Billing SDK documentation.
/// </summary>
public interface IMaxioGateway
{
    Task<MaxioProductFamily?> FindProductFamilyByHandleAsync(string handle, CancellationToken ct);

    Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(int productFamilyId, CancellationToken ct);

    /// <summary>
    /// Looks up a customer by its unique reference. Returns null when no
    /// customer has that reference.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct);

    Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken ct);

    /// <summary>
    /// Creates a subscription without card capture by using invoice-based
    /// payment collection.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, int productId, CancellationToken ct);

    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken ct);

    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken ct);
}
