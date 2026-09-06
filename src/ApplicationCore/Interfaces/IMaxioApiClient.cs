using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioApiClient
{
    Task<MaxioProductResponse?> GetProductAsync(string productHandle);
    Task<List<MaxioProductResponse>> ListProductsByFamilyAsync(string familyHandle);
    Task<MaxioCustomerResponse?> CreateOrGetCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<MaxioSubscriptionResponse?> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<MaxioSubscriptionResponse?> GetSubscriptionAsync(int subscriptionId);
    Task<List<MaxioSubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId);
}

public class MaxioProductResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class MaxioCustomerResponse
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class MaxioSubscriptionResponse
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public MaxioProductResponse? Product { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
