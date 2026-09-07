using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioApiClient
{
    Task<List<ProductDto>> GetProductsAsync(string? familyHandle = null);
    Task<CustomerDto?> GetOrCreateCustomerAsync(string userId, string firstName, string lastName, string email);
    Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<SubscriptionDto?> GetSubscriptionAsync(int subscriptionId);
    Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId);
}
