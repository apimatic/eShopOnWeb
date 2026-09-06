using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; } = new();

    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }
}
