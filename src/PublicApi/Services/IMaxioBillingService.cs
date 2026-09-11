using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioBillingService
{
    Task<MaxioCustomer?> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<SubscriptionInfo?> SubscribeAsync(string userId, string planHandle);
    Task<List<SubscriptionInfo>> GetMySubscriptionsAsync(string userId);
    Task<List<PlanInfo>> GetPlansAsync();
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

public class SubscriptionInfo
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string NextBillingAt { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public int CustomerId { get; set; }
}

public class PlanInfo
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
