using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public interface IMaxioBillingService
{
    Task<SubscriptionPlanDto[]> ListSubscriptionPlansAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string productHandle, CancellationToken cancellationToken = default);
    Task<SubscriptionDto[]> ListUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
    Task<SubscriptionDto> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTime CurrentPeriodStartsAt { get; set; }
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
}
