using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListPlansResponse : BaseResponse
{
    public ListPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
