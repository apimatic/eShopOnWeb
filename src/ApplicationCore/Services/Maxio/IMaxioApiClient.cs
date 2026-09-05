using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services.Maxio;

public interface IMaxioApiClient
{
    Task<MaxioProduct?> GetProductByHandleAsync(string handle);
    Task<List<MaxioProduct>> ListProductsByFamilyHandleAsync(string familyHandle);
    Task<MaxioCustomer> CreateOrGetCustomerAsync(string userId, string email, string? firstName, string? lastName);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId);
    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId);
}
