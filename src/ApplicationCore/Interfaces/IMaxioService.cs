using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioService
{
    Task<IEnumerable<MaxioProductDto>> GetProductsForFamilyAsync(string familyHandle);
    Task<MaxioCustomerDto?> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(long customerId, string productHandle);
    Task<IEnumerable<MaxioSubscriptionDto>> GetCustomerSubscriptionsAsync(long customerId);
}

public class MaxioProductDto
{
    public required long Id { get; set; }
    public required string Handle { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public required string IntervalUnit { get; set; }
}

public class MaxioCustomerDto
{
    public required long Id { get; set; }
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Reference { get; set; }
}

public class MaxioSubscriptionDto
{
    public required long Id { get; set; }
    public required long CustomerId { get; set; }
    public required long ProductId { get; set; }
    public string? ProductHandle { get; set; }
    public required string State { get; set; }
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
}
