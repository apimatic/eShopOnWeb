using Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin client for the Maxio (Advanced Billing) API. All Maxio interactions go through
/// this interface.
/// </summary>
public interface IMaxioClient
{
    /// <summary>Looks up a customer by its reference value. Returns null when not found.</summary>
    Task<MaxioCustomerDto?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates a customer.</summary>
    Task<MaxioCustomerDto> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists the products belonging to a product family (identified by id or "handle:...").</summary>
    Task<IReadOnlyList<MaxioProductDto>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>Reads a product by its API handle.</summary>
    Task<MaxioProductDto?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>Creates a subscription.</summary>
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads a single subscription.</summary>
    Task<MaxioSubscriptionDto> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions that belong to a customer.</summary>
    Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
