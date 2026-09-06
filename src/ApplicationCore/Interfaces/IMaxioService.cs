using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioService
{
    Task<SubscriptionPlanDto> GetPlanAsync(string planHandle);
    Task<IEnumerable<SubscriptionPlanDto>> GetPlansAsync();
    Task<CustomerDto?> GetOrCreateCustomerAsync(string email, string userId);
    Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<SubscriptionDto?> GetSubscriptionAsync(int subscriptionId);
    Task<IEnumerable<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId);
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Interval { get; set; } = string.Empty;
    public int IntervalUnit { get; set; }
}

public class CustomerDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal CurrentPeriodAmountInCents { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}
