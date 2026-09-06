using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    /// <summary>The plans currently offered, cheapest first.</summary>
    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new();
}
