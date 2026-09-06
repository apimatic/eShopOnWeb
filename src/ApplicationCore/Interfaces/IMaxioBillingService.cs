using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingService
{
    Task<List<MaxioProduct>> GetAvailablePlansAsync();
    Task<MaxioCustomer?> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId);
}
