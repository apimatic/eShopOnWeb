using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse()
    {
        SubscriptionPlans = new List<SubscriptionPlanDto>();
    }

    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; }
}
