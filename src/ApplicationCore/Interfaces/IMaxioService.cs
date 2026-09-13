using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default);
    Task<SubscriptionResultDto> SubscribeAsync(string userId, string email, string firstName, string lastName, string productHandle, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionDetailDto>> GetMySubscriptionsAsync(string userId, CancellationToken ct = default);
}

public class SubscriptionPlanDto
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public string? IntervalUnit { get; set; }
    public int? Interval { get; set; }
    public decimal Price => (PriceInCents ?? 0) / 100m;
}

public class SubscriptionResultDto
{
    public int? SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? PlanName { get; set; }
    public string? PlanHandle { get; set; }
    public long? PriceInCents { get; set; }
    public decimal Price => (PriceInCents ?? 0) / 100m;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int? CustomerId { get; set; }
}

public class SubscriptionDetailDto
{
    public int? SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? PlanName { get; set; }
    public string? PlanHandle { get; set; }
    public long? PriceInCents { get; set; }
    public decimal Price => (PriceInCents ?? 0) / 100m;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public string? Currency { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
}
