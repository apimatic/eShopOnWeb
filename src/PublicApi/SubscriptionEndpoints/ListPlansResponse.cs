using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListPlansResponse : BaseResponse
{
    public ListPlansResponse()
    {
    }

    public ListPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanDto> Plans { get; } = new();
}
