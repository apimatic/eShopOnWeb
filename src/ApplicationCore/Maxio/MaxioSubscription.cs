using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>A Maxio subscription, flattened to the fields the subscribe flow cares about.</summary>
public class MaxioSubscription
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string State { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
}
