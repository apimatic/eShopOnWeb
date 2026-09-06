using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionResponseDto
{
    public long SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string PlanName { get; set; } = null!;
    public decimal PriceInCents { get; set; }
    public string State { get; set; } = null!;
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
}
