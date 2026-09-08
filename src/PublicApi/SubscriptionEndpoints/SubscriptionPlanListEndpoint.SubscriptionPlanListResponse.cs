using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListResponse : Microsoft.eShopWeb.PublicApi.BaseResponse
{
    public SubscriptionPlanListResponse()
    {
    }

    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanDto> SubscriptionPlans { get; } = new();
}
