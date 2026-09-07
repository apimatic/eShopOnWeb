using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string? State { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long ProductPriceInCents { get; set; }
    public decimal ProductPriceDecimal => ProductPriceInCents / 100m;
}
