using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The list of subscription plans available for purchase.
/// </summary>
public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse()
    {
    }

    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new List<SubscriptionPlanDto>();
}