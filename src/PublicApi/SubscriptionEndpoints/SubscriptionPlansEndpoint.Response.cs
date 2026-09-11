using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlansResponse() { }
    public SubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public List<PlanDto> Plans { get; set; } = new();
}

public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
