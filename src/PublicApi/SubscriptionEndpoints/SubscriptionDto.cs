using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public string State { get; set; } = string.Empty;
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>When the next regularly scheduled charge happens (the end of the current billing period).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    /// <summary>When payment capture is next attempted; differs from <see cref="NextBillingAt"/> after a failed renewal.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto FromModel(SubscriptionDetails details) => new()
    {
        SubscriptionId = details.SubscriptionId,
        Reference = details.Reference,
        PlanHandle = details.PlanHandle,
        PlanName = details.PlanName,
        State = details.State,
        PriceInCents = details.PriceInCents,
        Price = details.PriceInCents / 100m,
        Currency = details.Currency,
        Interval = details.Interval,
        IntervalUnit = details.IntervalUnit,
        NextBillingAt = details.NextBillingAt,
        NextAssessmentAt = details.NextAssessmentAt,
        ActivatedAt = details.ActivatedAt,
        CreatedAt = details.CreatedAt
    };
}
