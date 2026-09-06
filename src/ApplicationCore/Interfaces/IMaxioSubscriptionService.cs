using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioSubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetAvailablePlansAsync();
    Task<CustomerSubscriptionDto?> CreateSubscriptionAsync(string userId, string userEmail, string firstName, string lastName, string planHandle);
    Task<List<CustomerSubscriptionDto>> GetUserSubscriptionsAsync(string userId);
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class CustomerSubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? NextBillingDate { get; set; }
}
