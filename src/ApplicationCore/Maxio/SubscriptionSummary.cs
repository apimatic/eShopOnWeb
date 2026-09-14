using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// The current state of a subscription, read live from the billing system of record.
/// </summary>
public class SubscriptionSummary
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
