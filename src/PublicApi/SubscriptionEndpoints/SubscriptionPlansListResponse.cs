using System.Collections.Generic;
using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Response for GET /api/subscription-plans.</summary>
public class SubscriptionPlansListResponse : BaseResponse
{
    public SubscriptionPlansListResponse()
    {
    }

    public SubscriptionPlansListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
