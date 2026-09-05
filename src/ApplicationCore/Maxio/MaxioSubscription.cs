using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class MaxioSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
