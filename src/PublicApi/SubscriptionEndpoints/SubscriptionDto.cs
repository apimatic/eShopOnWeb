using System;
using Microsoft.eShopWeb.PublicApi.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public string State { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }

    public static SubscriptionDto From(BillingSubscription s) => new()
    {
        Id = s.Id,
        Reference = s.Reference,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        State = s.State,
        IsActive = s.IsLive,
        PriceInCents = s.PriceInCents,
        Price = s.PriceInCents is long cents ? cents / 100m : null,
        Currency = s.Currency,
        Interval = s.Interval,
        IntervalUnit = s.IntervalUnit,
        NextBillingAt = s.NextBillingAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        CreatedAt = s.CreatedAt,
        ActivatedAt = s.ActivatedAt,
    };
}
