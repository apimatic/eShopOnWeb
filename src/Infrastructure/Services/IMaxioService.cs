using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioService
{
    Task<MaxioCustomerResponse?> GetOrCreateCustomerAsync(string reference, string firstName, string lastName, string email);
    Task<List<MaxioProductResponse>> ListProductsAsync();
    Task<MaxioSubscriptionResponse> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<MaxioSubscriptionResponse?> GetSubscriptionAsync(int subscriptionId);
    Task<List<MaxioSubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId);
}

public class MaxioCustomerResponse
{
    public int Id { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Reference { get; set; }
}

public class MaxioProductResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Handle { get; set; } = null!;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = null!;
}

public class MaxioSubscriptionResponse
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = null!;
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public MaxioProductResponse? Product { get; set; }
}
