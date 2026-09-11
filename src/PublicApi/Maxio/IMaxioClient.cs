using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Dto;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioClient
{
    Task<List<MaxioProductDto>> ListProductsAsync(string productFamilyHandle);
    Task<MaxioProductDto?> GetProductByHandleAsync(string handle);
    Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(string reference);
    Task<MaxioCustomerDto> CreateCustomerAsync(MaxioCreateCustomerRequest request);
    Task<MaxioCustomerDto> EnsureCustomerAsync(string reference, string firstName, string lastName, string email);
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request);
    Task<List<MaxioSubscriptionDto>> ListSubscriptionsAsync(string? state = null);
    Task<MaxioSubscriptionDto?> FindSubscriptionByReferenceAsync(string reference);
    Task<List<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId);
}
