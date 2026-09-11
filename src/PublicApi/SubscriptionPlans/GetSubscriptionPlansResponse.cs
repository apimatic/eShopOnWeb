using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlans;

public class GetSubscriptionPlansResponse : BaseResponse
{
    public GetSubscriptionPlansResponse() : base()
    {
    }

    public GetSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public int Interval { get; set; }
}
