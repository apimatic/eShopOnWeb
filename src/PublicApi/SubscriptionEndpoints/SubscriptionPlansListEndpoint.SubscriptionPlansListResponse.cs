using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansListResponse : BaseResponse
{
    public SubscriptionPlansListResponse() { }
    public SubscriptionPlansListResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
