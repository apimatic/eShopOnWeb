using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class SubscriptionDto
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public string? PlanName { get; set; }
    public string? PlanHandle { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
