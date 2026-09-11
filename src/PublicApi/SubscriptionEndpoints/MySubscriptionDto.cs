using System;

namespace Microsoft.eShopWeb.PublicApi;

public class MySubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTime? CreatedAt { get; set; }
}
