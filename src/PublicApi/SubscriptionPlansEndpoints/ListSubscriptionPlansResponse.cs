using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlansEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; } = new List<SubscriptionPlanDto>();

    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }
}
