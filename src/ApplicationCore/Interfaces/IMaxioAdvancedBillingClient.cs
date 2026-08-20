using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Typed client for the Maxio Advanced Billing REST API operations this integration uses.
/// Endpoints are those documented by Maxio (formerly Chargify): customers, subscriptions, product families.
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<SubscriptionPlan>> ListProductsInFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    Task<MaxioCustomerRecord?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomerRecord> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<ShopperSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string? reference, CancellationToken cancellationToken = default);
}
