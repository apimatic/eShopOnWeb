using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioService
{
    Task<MaxioProductDto> GetProductAsync(string handle);
    Task<IEnumerable<MaxioProductDto>> GetProductsAsync();
    Task<MaxioCustomerDto> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(string customerReference, string productHandle);
    Task<IEnumerable<MaxioSubscriptionDto>> ListSubscriptionsAsync(string customerReference);
    Task<MaxioSubscriptionDto> GetSubscriptionAsync(int subscriptionId);
}

public class MaxioProductDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

public class MaxioCustomerDto
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

public class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
}
