using System;

namespace Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;

/// <summary>
/// A subscription enrolled in Maxio.
/// </summary>
public class MaxioSubscription
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>When the current period ends — i.e. the date of the next regularly scheduled charge.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
