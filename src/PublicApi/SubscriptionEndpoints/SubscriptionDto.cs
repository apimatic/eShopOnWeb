using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as seen by the signed-in shopper.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string PlanHandle { get; set; } = "";
    public string PlanName { get; set; } = "";
    public int PriceInCents { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}
