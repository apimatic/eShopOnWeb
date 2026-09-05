using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class MaxioSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
}
