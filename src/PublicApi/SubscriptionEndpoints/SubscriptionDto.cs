using System;
using Microsoft.eShopWeb.PublicApi.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string? State { get; set; }
    /// <summary>When the next regularly scheduled charge will occur.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public static SubscriptionDto From(SubscriptionInfo s) => new()
    {
        Id = s.Id,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        Price = s.PriceInCents / 100m,
        Currency = s.Currency,
        State = s.State,
        NextBillingDate = s.CurrentPeriodEndsAt,
        NextAssessmentAt = s.NextAssessmentAt
    };
}
