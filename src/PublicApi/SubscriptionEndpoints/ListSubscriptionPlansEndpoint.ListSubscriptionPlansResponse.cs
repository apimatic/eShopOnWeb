using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response containing available subscription plans
/// </summary>
public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
        Plans = new List<SubscriptionPlanDto>();
    }

    public IList<SubscriptionPlanDto> Plans { get; set; }
}
