using System;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A customer's subscription as confirmed by Maxio Advanced Billing.</summary>
public class SubscriptionDto
{
    public long SubscriptionId { get; init; }
    public string State { get; init; } = string.Empty;
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public int PriceInCents { get; init; }
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string? Reference { get; init; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public static SubscriptionDto From(Subscription s) => new()
    {
        SubscriptionId = s.Id,
        State = s.State,
        PlanHandle = s.ProductHandle,
        PlanName = s.ProductName,
        PriceInCents = s.PriceInCents,
        Price = s.PriceInCents / 100m,
        Interval = s.Interval,
        IntervalUnit = s.IntervalUnit,
        Reference = s.Reference,
        CurrentPeriodStartedAt = s.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        NextBillingAt = s.NextAssessmentAt,
        CreatedAt = s.CreatedAt
    };
}
