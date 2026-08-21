using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin client for the Maxio Advanced Billing REST API operations this integration uses.
/// Paths and fields match the official Advanced Billing HTTP API.
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<MaxioProductInfo>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    Task<MaxioCustomerInfo?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<MaxioCustomerInfo> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string reference,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscriptionInfo>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    Task<MaxioSubscriptionInfo> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken = default);
}
