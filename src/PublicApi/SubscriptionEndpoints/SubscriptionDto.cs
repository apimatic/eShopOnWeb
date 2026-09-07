using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int ProductPriceInCents { get; set; }
    public DateTime? CurrentPeriodStartedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
}
