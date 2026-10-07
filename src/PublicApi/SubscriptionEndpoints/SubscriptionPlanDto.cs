using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan available for purchase.
/// </summary>
public class SubscriptionPlanDto
{
    public string PlanHandle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string Price { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class ListSubscriptionPlansResponse
{
    public string CorrelationId { get; set; } = string.Empty;
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
