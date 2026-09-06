using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingService
{
    Task<List<SubscriptionPlan>> GetSubscriptionPlansAsync();
    Task<MaxioCustomer> GetOrCreateCustomerAsync(string externalId, string firstName, string lastName, string email);
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle);
    Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId);
    Task<MaxioSubscription?> GetSubscriptionAsync(long subscriptionId);
}
