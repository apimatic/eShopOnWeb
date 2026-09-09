using System;
using System.Collections.Generic;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response for listing subscription plans.
/// </summary>
public class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();
}
