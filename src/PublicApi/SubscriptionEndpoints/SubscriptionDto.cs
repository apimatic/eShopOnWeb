using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    /// <summary>The billing system's state, e.g. <c>active</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>False once the subscription reached a terminal state (canceled, expired, failed to create).</summary>
    public bool IsLive { get; set; }

    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>When the next regularly scheduled charge is due (the end of the current billing period).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    /// <summary>When the billing system next assesses the subscription (differs from
    /// <see cref="NextBillingAt"/> while a failed payment is being retried).</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto From(SubscriptionSummary s) => new()
    {
        Id = s.Id,
        Reference = s.Reference,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        State = s.State,
        IsLive = s.IsLive,
        PriceInCents = s.PriceInCents,
        Price = s.PriceInCents / 100m,
        Currency = s.Currency,
        Interval = s.Interval,
        IntervalUnit = s.IntervalUnit,
        NextBillingAt = s.NextBillingAt,
        NextAssessmentAt = s.NextAssessmentAt,
        ActivatedAt = s.ActivatedAt,
        CreatedAt = s.CreatedAt
    };
}
