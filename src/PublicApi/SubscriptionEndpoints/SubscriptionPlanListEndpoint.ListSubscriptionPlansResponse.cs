using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response for listing subscription plans.
/// </summary>
public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
        SubscriptionPlans = new List<SubscriptionPlanDto>();
    }

    public ListSubscriptionPlansResponse() : base()
    {
        SubscriptionPlans = new List<SubscriptionPlanDto>();
    }

    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; }
}