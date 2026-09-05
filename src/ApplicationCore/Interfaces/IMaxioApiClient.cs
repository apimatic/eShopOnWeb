using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioApiClient
{
    Task<MaxioProduct?> GetProductByHandleAsync(string productHandle);
    Task<IEnumerable<MaxioProduct>> GetProductsByFamilyHandleAsync(string familyHandle);
    Task<MaxioCustomerResponse> CreateOrGetCustomerAsync(string reference, string firstName, string lastName, string email);
    Task<MaxioSubscriptionResponse> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<IEnumerable<MaxioSubscriptionResponse>> GetSubscriptionsByCustomerAsync(int customerId);
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class MaxioCustomerResponse
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MaxioSubscriptionResponse
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int ProductPriceInCents { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextAssessmentAt { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
