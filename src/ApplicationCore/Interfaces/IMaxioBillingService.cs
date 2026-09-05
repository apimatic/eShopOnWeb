using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingService
{
    Task<MaxioCustomerInfo?> GetOrCreateCustomerAsync(string userId, string firstName, string lastName, string email);
    Task<MaxioSubscriptionInfo?> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<List<MaxioSubscriptionInfo>> ListCustomerSubscriptionsAsync(int customerId);
    Task<List<MaxioProductInfo>> ListProductsAsync();
}

public class MaxioCustomerInfo
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class MaxioSubscriptionInfo
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public decimal PricePerBillingCycle { get; set; }
    public string BillingPeriod { get; set; } = string.Empty;
    public DateTime? NextBillingAt { get; set; }
}

public class MaxioProductInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal PricePerBillingCycle { get; set; }
    public string BillingPeriod { get; set; } = string.Empty;
}
