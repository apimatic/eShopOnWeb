using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansResponse : BaseResponse
{
    public GetSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
