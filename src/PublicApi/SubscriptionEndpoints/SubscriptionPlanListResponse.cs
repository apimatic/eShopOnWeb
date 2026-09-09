using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionPlanListResponse()
    {
    }

    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new List<SubscriptionPlanDto>();
}
