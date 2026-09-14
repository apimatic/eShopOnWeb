using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
        Plans = new List<SubscriptionPlanDto>();
    }

    public ListSubscriptionPlansResponse()
    {
        Plans = new List<SubscriptionPlanDto>();
    }

    public List<SubscriptionPlanDto> Plans { get; set; }
}
