using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioClient
{
    Task<MaxioProductDto> GetProductByHandleAsync(string productHandle);
    Task<List<MaxioProductDto>> ListProductsByFamilyHandleAsync(string familyHandle);
    Task<MaxioCustomerDto> CreateCustomerAsync(string email, string firstName, string lastName, string reference);
    Task<MaxioCustomerDto?> GetCustomerAsync(int customerId);
    Task<List<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId);
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, string paymentCollectionMethod = "automatic");
}

public class MaxioProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Handle { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = null!;
    public bool RequireCreditCard { get; set; }
    public int ProductFamilyId { get; set; }
}

public class MaxioCustomerDto
{
    public int Id { get; set; }
    public string Email { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = null!;
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public long ProductPriceInCents { get; set; }
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextAssessmentAt { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
