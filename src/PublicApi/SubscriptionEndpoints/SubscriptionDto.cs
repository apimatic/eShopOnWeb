using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class SubscriptionDto
{
    public int? SubscriptionId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public long? PriceInCents { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? NextAssessmentDate { get; set; }

    public static SubscriptionDto From(ApplicationCore.Interfaces.SubscriptionInfo i) => new()
    {
        SubscriptionId = i.SubscriptionId,
        PlanHandle = i.PlanHandle,
        PlanName = i.PlanName,
        PriceInCents = i.PriceInCents,
        Price = i.PriceInCents / 100m,
        State = i.State,
        NextBillingDate = i.NextBillingAt,
        NextAssessmentDate = i.NextAssessmentAt,
    };
}
