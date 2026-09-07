using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<IEnumerable<SubscriptionPlanDto>> ListProductsAsync();
    Task<CustomerDto> GetOrCreateCustomerAsync(string userId, string firstName, string lastName, string email);
    Task<SubscriptionDto> CreateSubscriptionAsync(string customerId, string productHandle);
    Task<IEnumerable<SubscriptionDto>> ListCustomerSubscriptionsAsync(string customerId);
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "";
}

public class CustomerDto
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string ProductHandle { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string State { get; set; } = "";
    public DateTime? ActivatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public decimal? CurrentPeriodAmountInCents { get; set; }
}
