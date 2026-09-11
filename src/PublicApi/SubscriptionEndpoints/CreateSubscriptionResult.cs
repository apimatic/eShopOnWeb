using System;

namespace Microsoft.eShopWeb.PublicApi;

public class CreateSubscriptionResult
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int CustomerId { get; set; }
}
