using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Minimal typed client over the Maxio Advanced Billing REST API.
/// </summary>
public interface IMaxioClient
{
    /// <summary>Lists products in a product family, addressed by the family's handle.</summary>
    Task<IReadOnlyList<MaxioProductDto>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken);

    /// <summary>Finds a customer by its external reference; returns null when absent (404).</summary>
    Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomerDto> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscriptionDto>> ListSubscriptionsAsync(long customerId, CancellationToken cancellationToken);

    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken);
}
