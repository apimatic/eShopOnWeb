using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public interface IMaxioApiClient
{
    Task<IReadOnlyList<MaxioProductDto>> GetProductsByFamilyAsync(string productFamilyHandle);
    Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(string reference);
    Task<MaxioCustomerDto> CreateCustomerAsync(string firstName, string lastName, string email, string reference);
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request);
    Task<IReadOnlyList<MaxioSubscriptionDto>> GetSubscriptionsByCustomerIdAsync(int maxioCustomerId);
}
