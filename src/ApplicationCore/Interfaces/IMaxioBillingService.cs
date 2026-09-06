using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingService
{
    Task<SubscriptionPlanDto?> GetPlanByHandleAsync(string handle);
    Task<List<SubscriptionPlanDto>> GetPlansAsync(string productFamilyHandle);
    Task<SubscriptionDto?> CreateSubscriptionAsync(string userId, string planHandle, string? customerReference = null);
    Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(string customerReference);
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public int? ProductId { get; set; }
    public string? ProductHandle { get; set; }
    public CustomerDto? Customer { get; set; }
    public ProductDto? Product { get; set; }
}

public class CustomerDto
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class ProductDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
