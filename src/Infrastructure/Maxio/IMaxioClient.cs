using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioProductDto>> ListProductsAsync(string productFamilyHandle, CancellationToken ct = default);
    Task<MaxioCustomerDto?> LookupCustomerByReferenceAsync(string reference, CancellationToken ct = default);
    Task<MaxioCustomerDto> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken ct = default);
    Task<MaxioPaymentProfileDto> CreatePaymentProfileAsync(int customerId, MaxioCreatePaymentProfileRequest request, CancellationToken ct = default);
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
}
