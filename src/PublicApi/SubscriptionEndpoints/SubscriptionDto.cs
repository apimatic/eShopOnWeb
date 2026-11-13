using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? State { get; set; }

    /// <summary>When the current billing period ends and the next regular charge is scheduled.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    /// <summary>When Maxio will next attempt to assess/capture payment (differs from <see cref="NextBillingAt"/> after a failed renewal).</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto From(SubscriptionDetails s) => new()
    {
        Id = s.Id,
        Reference = s.Reference,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        Price = s.PriceInCents / 100m,
        PriceInCents = s.PriceInCents,
        Interval = s.Interval,
        IntervalUnit = s.IntervalUnit,
        State = s.State,
        NextBillingAt = s.NextBillingAt,
        NextAssessmentAt = s.NextAssessmentAt,
        CreatedAt = s.CreatedAt
    };
}
