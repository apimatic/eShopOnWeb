using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionPlanListResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();

    /// <summary>
    /// True when the plan walk hit its page cap before the billing system signalled the last
    /// page — the list may be incomplete.
    /// </summary>
    public bool Truncated { get; set; }
}