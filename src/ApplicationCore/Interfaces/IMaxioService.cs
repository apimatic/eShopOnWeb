using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioService
{
    Task<MaxioProductDto?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default);
    Task<IEnumerable<MaxioProductDto>> ListProductsByFamilyHandleAsync(string familyHandle, CancellationToken cancellationToken = default);
    Task<MaxioCustomerDto?> GetOrCreateCustomerAsync(string customerId, string email, string firstName, string lastName, CancellationToken cancellationToken = default);
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default);
    Task<MaxioSubscriptionDto?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
    Task<IEnumerable<MaxioSubscriptionDto>> ListSubscriptionsByCustomerAsync(int customerId, CancellationToken cancellationToken = default);
}

public class MaxioProductDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequiresCreditCard { get; set; }
}

public class MaxioCustomerDto
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Reference { get; set; }
}

public class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public required string State { get; set; }
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MaxioCreateSubscriptionRequest
{
    public int? CustomerId { get; set; }
    public string? ProductHandle { get; set; }
    public int? ProductId { get; set; }
}
