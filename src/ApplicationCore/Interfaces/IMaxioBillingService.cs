using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingService
{
    Task<MaxioProduct> GetProductByHandleAsync(string handle);
    Task<List<MaxioProduct>> GetProductsForFamilyAsync();
    Task<MaxioCustomer> GetOrCreateCustomerAsync(string email, string firstName, string lastName, string reference);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId);
    Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId);
    Task<MaxioSubscription> GetSubscriptionAsync(int subscriptionId);
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
}
