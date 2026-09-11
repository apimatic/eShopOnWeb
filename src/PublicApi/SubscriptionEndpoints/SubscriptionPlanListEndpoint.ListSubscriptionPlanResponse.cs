using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlanResponse : BaseResponse
{
    public ListSubscriptionPlanResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlanResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
