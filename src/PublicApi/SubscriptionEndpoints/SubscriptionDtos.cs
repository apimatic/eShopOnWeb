using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    public static SubscriptionPlanDto From(SubscriptionPlan p) => new()
    {
        Handle = p.Handle,
        Name = p.Name,
        PriceInCents = p.PriceInCents,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit,
        IsDefault = p.IsDefault
    };
}

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    /// <summary>Provider state text, e.g. "active". Unknown values pass through unchanged.</summary>
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public static SubscriptionDto From(SubscriptionInfo s) => new()
    {
        SubscriptionId = s.SubscriptionId,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        PriceInCents = s.PriceInCents,
        Interval = s.Interval,
        IntervalUnit = s.IntervalUnit,
        State = s.State,
        NextBillingDate = s.NextBillingDate,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
    };
}
