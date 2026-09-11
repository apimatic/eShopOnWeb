using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public int PriceInCents { get; set; }
    public string Price => $"{PriceInCents / 100.0:F2}";
    public string IntervalUnit { get; set; } = "";
}
