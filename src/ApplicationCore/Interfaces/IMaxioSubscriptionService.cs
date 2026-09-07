using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class ProductDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class SubscriptionDataDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string State { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public interface IMaxioSubscriptionService
{
    Task<int> EnsureCustomerAsync(string userId, string email, CancellationToken ct = default);
    Task<List<ProductDto>> FetchPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDataDto> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default);
    Task<SubscriptionDataDto> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default);
    Task<List<SubscriptionDataDto>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
}
