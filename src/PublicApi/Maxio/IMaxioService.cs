using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default);
    Task<int> EnsureCustomerExistsAsync(string userId, string email, string firstName, string lastName, CancellationToken ct = default);
    Task<SubscriptionResultDto> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionDetailDto>> ListMySubscriptionsAsync(int maxioCustomerId, CancellationToken ct = default);
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public bool RequireCreditCard { get; set; }
}

public class SubscriptionResultDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public string ProductName { get; set; } = string.Empty;
}

public class SubscriptionDetailDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
}
